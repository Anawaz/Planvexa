"use client";

import { useSyncExternalStore } from "react";

/**
 * `navigator.onLine` alone is unreliable (true whenever the network interface is up, even with no real
 * route to the API — e.g. captive portals, VPN drops). Treat it as the fast/optimistic signal, but let
 * an actual failed `fetch()` (see `withOfflineFallback.ts`'s `isNetworkError`) flip it to false, and let
 * the `online` browser event flip it back. This is the single source of truth `useOnlineStatus` and the
 * outbox replay trigger both read.
 */
let online = typeof navigator === "undefined" ? true : navigator.onLine;
const listeners = new Set<() => void>();

function setOnline(value: boolean) {
  if (value === online) return;
  online = value;
  for (const listener of listeners) listener();
}

if (typeof window !== "undefined") {
  window.addEventListener("online", () => setOnline(true));
  window.addEventListener("offline", () => setOnline(false));
}

export function isOnline() {
  return online;
}

/** Called after a `fetch()` throws a network-level error (see `isNetworkError`) — corrects
 * `navigator.onLine` false positives instead of waiting for the `offline` event, which some browsers
 * never fire when only the API route (not the whole network) is unreachable. */
export function markOffline() {
  setOnline(false);
}

export function markOnline() {
  setOnline(true);
}

export function subscribeOnlineStatus(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** Live connectivity state for banners/badges (e.g. the app-shell "You're offline" indicator). */
export function useOnlineStatus() {
  return useSyncExternalStore(subscribeOnlineStatus, () => online, () => true);
}
