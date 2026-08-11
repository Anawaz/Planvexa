"use client";

/** Registers `public/sw.js` once per session. Called from `ServiceWorkerRegistration` (mounted in the
 * root layout, so it runs on every route — public/marketing pages get an installable/offline shell
 * too, not just the authenticated app). No-op in unsupported browsers or non-HTTPS/non-localhost
 * origins (the browser itself refuses registration there — nothing to feature-detect beyond
 * `serviceWorker in navigator`). */
export function registerServiceWorker() {
  if (typeof window === "undefined" || !("serviceWorker" in navigator)) return;

  window.addEventListener("load", () => {
    void navigator.serviceWorker.register("/sw.js").catch(() => undefined);
  });
}

/** Best-effort Background Sync registration (Chromium-only API) — see sw.js's `sync` handler doc
 * comment for why this is a bonus trigger, not the primary replay mechanism. */
export async function requestBackgroundSync() {
  try {
    const registration = await navigator.serviceWorker?.ready;
    const syncManager = (registration as unknown as { sync?: { register(tag: string): Promise<void> } })?.sync;
    await syncManager?.register("planvexa-outbox");
  } catch {
    // Background Sync unsupported or registration failed -- the online/reconnect triggers still cover it.
  }
}
