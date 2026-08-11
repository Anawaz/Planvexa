"use client";

import { useEffect } from "react";
import { registerServiceWorker } from "@/lib/offline/registerServiceWorker";

/** Mounted once in the root layout so every route (not just the authenticated app shell) is covered
 * by the installable/offline service worker. Renders nothing. */
export function ServiceWorkerRegistration() {
  useEffect(() => {
    registerServiceWorker();
  }, []);
  return null;
}
