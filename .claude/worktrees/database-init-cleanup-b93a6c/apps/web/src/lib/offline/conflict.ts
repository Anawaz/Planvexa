/**
 * Conflict detection for offline task edits.
 *
 * What exists already: `WorkItem` (the backend task entity) has no optimistic-concurrency column —
 * no `RowVersion`/`ConcurrencyToken`/`ETag`, confirmed by grepping the WorkManagement module. `PATCH
 * /tasks/{id}` is a plain last-write-wins update. Per the design brief, that means the minimal, honest
 * option is what's implemented here: last-write-wins (the queued PATCH is always applied — we do not
 * invent new backend versioning infrastructure for this), with a surfaced warning when a field the
 * offline patch does NOT touch also differs between the task as it was when the edit was queued
 * (`baseSnapshot`) and the task's state on the server right before the patch is replayed. That
 * difference means someone else changed the task while this client was offline.
 *
 * Only "content" fields are compared — not every field on the wire shape (assignee/tag id arrays would
 * produce noisy false positives on ordering alone; out of scope for a minimal warning).
 */

const COMPARABLE_FIELDS = ["title", "description", "statusId", "priority", "dueDate", "startDate"] as const;
type ComparableField = (typeof COMPARABLE_FIELDS)[number];

export type ConflictCheckResult = {
  hasConflict: boolean;
  fields: ComparableField[];
  message: string;
};

/**
 * @param baseSnapshot the task as last known locally when the offline edit was queued
 * @param serverCurrent the task as fetched from the server right before replaying the queued patch
 * @param patchFields the fields the queued patch itself is about to overwrite — excluded, since the
 *   offline client's own edit intentionally supersedes those regardless of what the server has.
 */
export function detectConflict(
  baseSnapshot: Record<string, unknown>,
  serverCurrent: Record<string, unknown>,
  patchFields: readonly string[],
): ConflictCheckResult {
  const changed = COMPARABLE_FIELDS.filter((field) => {
    if (patchFields.includes(field)) return false;
    if (!(field in baseSnapshot)) return false;
    return baseSnapshot[field] !== serverCurrent[field];
  });

  return {
    hasConflict: changed.length > 0,
    fields: changed,
    message:
      changed.length > 0
        ? `This task was changed by someone else while you were offline (${changed.join(", ")}). Your edit was applied on top of their changes.`
        : "",
  };
}
