"use client";

import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { useMembers } from "@/lib/members";
import { bulkUpdateTasks, deleteTask, restoreTask } from "@/lib/work/client";
import { useWorkMutation } from "@/lib/work/mutations";
import type { StatusDefinition } from "@/lib/work/types";
import { sortStatuses } from "./helpers";

const fieldClassName =
  "h-9 rounded-lg border border-border bg-background px-2 text-sm text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";

type BulkActionBarProps = {
  selectedIds: string[];
  statuses: StatusDefinition[];
  onClear: () => void;
};

/**
 * Sticky action bar for the current row selection. Status/assignee/due-date go through
 * `POST /tasks/bulk`; delete has no bulk route so it fans out one soft delete per task and offers
 * an undo that fans `POST /tasks/{id}/restore` back out.
 */
export function BulkActionBar({ selectedIds, statuses, onClear }: BulkActionBarProps) {
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const [deletedIds, setDeletedIds] = useState<string[]>([]);
  const members = useMembers().data ?? [];
  const bulk = useWorkMutation(bulkUpdateTasks);
  const remove = useWorkMutation(async (ids: string[]) => {
    await Promise.all(ids.map(deleteTask));
    return ids;
  });
  const undo = useWorkMutation(async (ids: string[]) => {
    await Promise.all(ids.map(restoreTask));
  });
  const error = (bulk.error ?? remove.error ?? undo.error) as Error | undefined;
  const busy = bulk.isPending || remove.isPending || undo.isPending;

  if (selectedIds.length === 0 && deletedIds.length === 0) {
    return null;
  }

  return (
    <div className="sticky bottom-4 z-30 rounded-[var(--radius)] border border-border bg-card p-3 shadow-lg">
      {error ? (
        <p role="alert" className="mb-2 text-sm text-red-600 dark:text-red-400">
          Bulk action failed: {error.message}
        </p>
      ) : null}
      {selectedIds.length > 0 ? (
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm font-semibold" aria-live="polite">
            {selectedIds.length} selected
          </span>
          <label className="text-xs text-muted-foreground">
            <span className="sr-only">Set status for selected tasks</span>
            <select
              value=""
              disabled={busy}
              className={fieldClassName}
              onChange={(event) => {
                if (event.target.value) {
                  bulk.mutate({ taskIds: selectedIds, statusId: event.target.value });
                }
              }}
            >
              <option value="">Set status…</option>
              {sortStatuses(statuses).map((status) => (
                <option key={status.id} value={status.id}>
                  {status.name}
                </option>
              ))}
            </select>
          </label>
          <label className="text-xs text-muted-foreground">
            <span className="sr-only">Assign selected tasks</span>
            <select
              value=""
              disabled={busy}
              className={fieldClassName}
              onChange={(event) => {
                if (event.target.value) {
                  bulk.mutate({ taskIds: selectedIds, addAssigneeUserId: event.target.value });
                }
              }}
            >
              <option value="">Assign…</option>
              {members.map((member) => (
                <option key={member.userId} value={member.userId}>
                  {member.displayName || member.email || member.userId}
                </option>
              ))}
            </select>
          </label>
          <label className="inline-flex items-center gap-2 text-xs text-muted-foreground">
            Due date
            <input
              type="date"
              disabled={busy}
              aria-label="Set due date for selected tasks"
              className={fieldClassName}
              onChange={(event) => {
                if (event.currentTarget.value) {
                  bulk.mutate({ taskIds: selectedIds, dueDate: event.currentTarget.value });
                }
              }}
            />
          </label>
          <span className="ml-auto inline-flex items-center gap-2">
            {confirmingDelete ? (
              <>
                <span className="text-xs text-muted-foreground">
                  Delete {selectedIds.length} task{selectedIds.length === 1 ? "" : "s"}?
                </span>
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  disabled={busy}
                  className="border-red-300 text-red-700 dark:border-red-900 dark:text-red-400"
                  onClick={() =>
                    remove.mutate(selectedIds, {
                      onSuccess: (ids) => {
                        setDeletedIds(ids);
                        setConfirmingDelete(false);
                        onClear();
                      },
                    })
                  }
                >
                  Confirm
                </Button>
                <Button
                  type="button"
                  size="sm"
                  variant="ghost"
                  onClick={() => setConfirmingDelete(false)}
                >
                  Cancel
                </Button>
              </>
            ) : (
              <Button
                type="button"
                size="sm"
                variant="ghost"
                disabled={busy}
                className="text-red-600 hover:text-red-700 dark:text-red-400"
                onClick={() => setConfirmingDelete(true)}
              >
                Delete
              </Button>
            )}
            <Button type="button" size="sm" variant="secondary" onClick={onClear}>
              Clear
            </Button>
          </span>
        </div>
      ) : null}
      {deletedIds.length > 0 ? (
        <div className="flex flex-wrap items-center gap-3 pt-2 text-sm">
          <span aria-live="polite">
            Deleted {deletedIds.length} task{deletedIds.length === 1 ? "" : "s"}.
          </span>
          <Button
            type="button"
            size="sm"
            variant="secondary"
            disabled={busy}
            onClick={() => undo.mutate(deletedIds, { onSuccess: () => setDeletedIds([]) })}
          >
            Undo
          </Button>
          <Button type="button" size="sm" variant="ghost" onClick={() => setDeletedIds([])}>
            Dismiss
          </Button>
        </div>
      ) : null}
    </div>
  );
}
