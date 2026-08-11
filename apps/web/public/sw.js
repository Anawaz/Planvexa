/**
 * Planvexa service worker.
 *
 * Tooling choice: hand-rolled, no Workbox. Reasoning: `next-pwa` (the common Workbox-on-Next.js
 * wrapper) targets the Pages Router / webpack build pipeline and is not maintained for the Next.js 16
 * App Router + Turbopack setup this app uses; wiring Workbox's build-time `injectManifest` step
 * ourselves would mean fighting Turbopack's asset pipeline for a feature this small. The current
 * Next.js App Router guidance (nextjs.org/docs — Guides / Progressive Web Apps) is exactly this: a
 * plain static file under `public/`, registered from a client component, no build step. The scope here
 * (cache a short, explicit app-shell list + let the app's own IndexedDB layer own offline task/comment/
 * time-entry data — see src/lib/offline/) does not need Workbox's routing DSL.
 *
 * Caching strategy, deliberately conservative:
 *  - App shell (this file's own small, explicit list): cache-first, refreshed on every `install`.
 *  - `/_next/static/*` build assets: cache-first (immutable, content-hashed by Next itself).
 *  - Everything else same-origin GET (pages, images): network-first, falling back to cache only when
 *    the network genuinely fails — never serves a stale page over a working connection.
 *  - `/api/proxy/*` (the backend API): NEVER cached here. API reads are cached in the app's
 *    workspace-scoped IndexedDB store instead (src/lib/offline/db.ts), which understands the data
 *    shape and workspace isolation rules; a second, opaque HTTP-level cache of the same responses
 *    would just be a second source of truth to keep consistent for no benefit.
 */

const CACHE_VERSION = "v1";
const SHELL_CACHE = `planvexa-shell-${CACHE_VERSION}`;
const APP_SHELL = ["/", "/manifest.webmanifest", "/icons/icon-192.png", "/icons/icon-512.png"];

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches
      .open(SHELL_CACHE)
      .then((cache) => cache.addAll(APP_SHELL))
      .then(() => self.skipWaiting()),
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((key) => key !== SHELL_CACHE).map((key) => caches.delete(key))))
      .then(() => self.clients.claim()),
  );
});

self.addEventListener("fetch", (event) => {
  const { request } = event;
  if (request.method !== "GET") return; // mutations always go live-or-outbox, never through the SW cache

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) return;
  if (url.pathname.startsWith("/api/")) return; // see file header: API responses are not SW-cached

  if (url.pathname.startsWith("/_next/static/")) {
    event.respondWith(cacheFirst(request));
    return;
  }

  event.respondWith(networkFirst(request));
});

async function cacheFirst(request) {
  const cached = await caches.match(request);
  if (cached) return cached;
  const response = await fetch(request);
  if (response.ok) {
    const cache = await caches.open(SHELL_CACHE);
    void cache.put(request, response.clone());
  }
  return response;
}

async function networkFirst(request) {
  try {
    const response = await fetch(request);
    if (response.ok) {
      const cache = await caches.open(SHELL_CACHE);
      void cache.put(request, response.clone());
    }
    return response;
  } catch {
    const cached = await caches.match(request);
    if (cached) return cached;
    if (request.mode === "navigate") {
      const shell = await caches.match("/");
      if (shell) return shell;
    }
    throw new Error("Offline and not cached.");
  }
}

/**
 * Background Sync API (Chromium-only; Safari/Firefox have no support) — best-effort only. The
 * primary outbox-replay triggers are the browser `online` event and the SignalR realtime reconnect,
 * both handled entirely in the page (src/lib/offline/useOfflineSync.ts, useRealtime.ts), because they
 * work everywhere and have direct access to the outbox/react-query without a postMessage round trip.
 * When this DOES fire, all it does is nudge any open tab to run the same replay immediately.
 */
self.addEventListener("sync", (event) => {
  if (event.tag !== "planvexa-outbox") return;
  event.waitUntil(
    self.clients.matchAll({ type: "window" }).then((clients) => {
      for (const client of clients) client.postMessage({ type: "sync-outbox" });
    }),
  );
});

/** Web Push (frontend half — see LoggingPushSender.cs's doc comment for the backend
 * half). Shows a real browser notification for whatever the backend's push sender sends once that
 * side is wired up to real delivery; today only LoggingPushSender (log-only) runs server-side, so this
 * handler is exercised by manual/test pushes, not production traffic yet. */
self.addEventListener("push", (event) => {
  let payload = { title: "Planvexa", body: "You have a new notification." };
  if (event.data) {
    try {
      payload = { ...payload, ...event.data.json() };
    } catch {
      payload.body = event.data.text();
    }
  }

  event.waitUntil(
    self.registration.showNotification(payload.title, {
      body: payload.body,
      icon: "/icons/icon-192.png",
      badge: "/icons/icon-192.png",
      data: { url: payload.url ?? "/app" },
    }),
  );
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const targetUrl = event.notification.data?.url ?? "/app";
  event.waitUntil(
    self.clients.matchAll({ type: "window" }).then((clients) => {
      const existing = clients.find((client) => client.url.includes(targetUrl));
      if (existing) return existing.focus();
      return self.clients.openWindow(targetUrl);
    }),
  );
});
