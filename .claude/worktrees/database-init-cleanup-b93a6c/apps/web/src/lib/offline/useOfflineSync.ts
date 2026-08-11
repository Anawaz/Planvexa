"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { replayOutbox } from "./replay";
import { markOnline, markOffline } from "./connectivity";

/**
 * Mounted once in the authenticated app shell (app/app/layout.tsx). Two reconnect triggers drive an
 * outbox replay: the browser `online` event here, and the SignalR hub's `onreconnected` in
 * useRealtime.ts. Also replays once on mount, in case items were queued in a previous tab/session and
 * connectivity is already back by the time this tab loads.
 */
export function useOfflineSync() {
  const queryClient = useQueryClient();

  useEffect(() => {
    void replayOutbox(queryClient);

    function handleOnline() {
      markOnline();
      void replayOutbox(queryClient);
    }
    function handleOffline() {
      markOffline();
    }

    window.addEventListener("online", handleOnline);
    window.addEventListener("offline", handleOffline);

    // Best-effort nudge from the service worker's own `sync` event handler (see public/sw.js) — the
    // Background Sync API only fires while the SW is alive and the browser chooses to honor it
    // (Chromium-only), so this is a bonus trigger on top of the two above, not the primary mechanism.
    function handleMessage(event: MessageEvent) {
      if (event.data?.type === "sync-outbox") {
        void replayOutbox(queryClient);
      }
    }
    navigator.serviceWorker?.addEventListener("message", handleMessage);

    return () => {
      window.removeEventListener("online", handleOnline);
      window.removeEventListener("offline", handleOffline);
      navigator.serviceWorker?.removeEventListener("message", handleMessage);
    };
  }, [queryClient]);
}
