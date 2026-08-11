"use client";

import { useEffect, useState } from "react";
import { conflictDismiss, conflictListByWorkspace, outboxListByWorkspace, subscribeOutboxChanges, type ConflictWarning, type OutboxItem } from "./db";

/** Live "pending sync" state for the active workspace — the outbox badge/banner and per-task "pending
 * sync" indicators both read this. Re-reads IndexedDB whenever `db.ts` reports an outbox write. */
export function useOutboxStatus(workspaceId: string | undefined) {
  const [items, setItems] = useState<OutboxItem[]>([]);
  const [conflicts, setConflicts] = useState<ConflictWarning[]>([]);

  useEffect(() => {
    if (!workspaceId) return;

    let cancelled = false;
    async function refresh() {
      const [outbox, conflictList] = await Promise.all([
        outboxListByWorkspace(workspaceId as string),
        conflictListByWorkspace(workspaceId as string),
      ]);
      if (!cancelled) {
        setItems(outbox);
        setConflicts(conflictList);
      }
    }

    void refresh();
    const unsubscribe = subscribeOutboxChanges(() => void refresh());
    return () => {
      cancelled = true;
      unsubscribe();
    };
  }, [workspaceId]);

  // Guarded by `workspaceId` rather than reset in the effect above: a synchronous setState in an
  // effect body for the "no workspace" case would trigger a cascading extra render for no benefit --
  // stale state simply isn't exposed while there is no active workspace to scope it to.
  const scopedItems = workspaceId ? items : [];
  const scopedConflicts = workspaceId ? conflicts : [];

  return {
    pendingCount: scopedItems.filter((item) => item.status !== "error").length,
    errorCount: scopedItems.filter((item) => item.status === "error").length,
    items: scopedItems,
    conflicts: scopedConflicts,
    dismissConflict: conflictDismiss,
  };
}
