"use client";

import { useAppContext } from "@/lib/app-context/AppContext";
import { outboxRemove, outboxUpdate } from "@/lib/offline/db";
import { useOnlineStatus } from "@/lib/offline/connectivity";
import { useOutboxStatus } from "@/lib/offline/useOutboxStatus";
import { Button } from "@/components/ui/Button";

/** Resets a failed outbox item back to "pending" so the replay loop (see replay.ts) picks it up
 * again on the next sync pass — reuses the existing generic patch function rather than adding a
 * dedicated "retry" store method. */
function retryOutboxItem(id: string) {
  void outboxUpdate(id, { status: "pending", error: undefined });
}

/**  . uc(a) thin banner under the topbar — "you're offline" while disconnected, and a "N pending
 * sync" / conflict summary whenever the outbox has anything queued for the active workspace, even
 * after coming back online (a replay can still be mid-flight or blocked on an error). Expands to
 * list each conflict/failed item with a dismiss/retry/discard action. */
export function OfflineIndicator() {
  const isOnline = useOnlineStatus();
  const { workspaceId } = useAppContext();
  const { pendingCount, errorCount, conflicts, items, dismissConflict } = useOutboxStatus(workspaceId);
  const failedItems = items.filter((item) => item.status === "error");

  if (isOnline && pendingCount === 0 && errorCount === 0 && conflicts.length === 0) {
    return null;
  }

  return (
    <div className="border-b border-border bg-amber-50 px-6 py-2 text-xs font-medium text-amber-900 dark:bg-amber-950 dark:text-amber-200 sm:px-8 lg:px-10">
      <div role="status" className="flex flex-wrap items-center gap-x-4 gap-y-1">
        {!isOnline ? <span>You&apos;re offline — changes are being saved locally.</span> : null}
        {pendingCount > 0 ? <span>{pendingCount} change{pendingCount === 1 ? "" : "s"} pending sync</span> : null}
        {errorCount > 0 ? <span className="text-red-700 dark:text-red-300">{errorCount} failed to sync</span> : null}
        {conflicts.length > 0 ? (
          <span className="text-red-700 dark:text-red-300">
            {conflicts.length} task{conflicts.length === 1 ? "" : "s"} changed by someone else while you were offline
          </span>
        ) : null}
      </div>

      {conflicts.length > 0 || failedItems.length > 0 ? (
        <ul className="mt-2 flex flex-col gap-1.5">
          {conflicts.map((conflict) => (
            <li
              key={conflict.id}
              className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-amber-200 bg-white/60 px-2 py-1.5 dark:border-amber-900 dark:bg-black/20"
            >
              <span>{conflict.message}</span>
              <Button size="sm" variant="outline" onClick={() => dismissConflict(conflict.id)}>
                Dismiss
              </Button>
            </li>
          ))}
          {failedItems.map((item) => (
            <li
              key={item.id}
              className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-amber-200 bg-white/60 px-2 py-1.5 dark:border-amber-900 dark:bg-black/20"
            >
              <span className="text-red-700 dark:text-red-300">
                {item.type} failed to sync{item.error ? `: ${item.error}` : ""}
              </span>
              <div className="flex gap-2">
                <Button size="sm" variant="outline" onClick={() => retryOutboxItem(item.id)}>
                  Retry
                </Button>
                <Button size="sm" variant="outline" onClick={() => outboxRemove(item.id)}>
                  Discard
                </Button>
              </div>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
