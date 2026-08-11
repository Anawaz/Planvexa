"use client";

import { useAppContext } from "@/lib/app-context/AppContext";
import { useOnlineStatus } from "@/lib/offline/connectivity";
import { useOutboxStatus } from "@/lib/offline/useOutboxStatus";

/**  . uc(a) thin banner under the topbar — "you're offline" while disconnected, and a "N pending
 * sync" / conflict summary whenever the outbox has anything queued for the active workspace, even
 * after coming back online (a replay can still be mid-flight or blocked on an error). */
export function OfflineIndicator() {
  const isOnline = useOnlineStatus();
  const { workspaceId } = useAppContext();
  const { pendingCount, errorCount, conflicts } = useOutboxStatus(workspaceId);

  if (isOnline && pendingCount === 0 && errorCount === 0 && conflicts.length === 0) {
    return null;
  }

  return (
    <div
      role="status"
      className="flex flex-wrap items-center gap-x-4 gap-y-1 border-b border-border bg-amber-50 px-6 py-2 text-xs font-medium text-amber-900 dark:bg-amber-950 dark:text-amber-200 sm:px-8 lg:px-10"
    >
      {!isOnline ? <span>You&apos;re offline — changes are being saved locally.</span> : null}
      {pendingCount > 0 ? <span>{pendingCount} change{pendingCount === 1 ? "" : "s"} pending sync</span> : null}
      {errorCount > 0 ? <span className="text-red-700 dark:text-red-300">{errorCount} failed to sync</span> : null}
      {conflicts.length > 0 ? (
        <span className="text-red-700 dark:text-red-300">
          {conflicts.length} task{conflicts.length === 1 ? "" : "s"} changed by someone else while you were offline
        </span>
      ) : null}
    </div>
  );
}
