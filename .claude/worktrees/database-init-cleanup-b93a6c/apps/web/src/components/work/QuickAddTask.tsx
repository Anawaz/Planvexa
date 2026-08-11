"use client";

import { useQuery } from "@tanstack/react-query";
import { useId, useRef, useState } from "react";
import { Button } from "@/components/ui/Button";
import { useMembers } from "@/lib/members";
import { listLists, listSpaces, listStatusSchemes } from "@/lib/work/client";
import { createTaskOffline as createTask } from "@/lib/work/offlineMutations";
import { useWorkMutation } from "@/lib/work/mutations";
import { workKeys } from "@/lib/work/queries";
import type { StatusDefinition } from "@/lib/work/types";
import { sortStatuses } from "./helpers";
import { useFocusTrap } from "./useFocusTrap";

const fieldClassName =
  "h-9 w-full rounded-lg border border-border bg-background px-2 text-sm font-normal text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";

type QuickAddTaskProps = {
  onClose: () => void;
  /** Fixed target list. Omit to let the dialog show a space/list picker (My Work, command palette). */
  listId?: string;
  statuses?: StatusDefinition[];
  parentId?: string;
  onCreated?: (taskId: string) => void;
};

/**
 * The one "New task" form. Title is the only required field; status, assignee and due date are
 * there so a task can be filed correctly without opening the detail drawer afterwards.
 * Render it conditionally — mounting is what opens it, so every open starts on a blank form.
 */
export function QuickAddTask({
  onClose,
  listId,
  statuses,
  parentId,
  onCreated,
}: QuickAddTaskProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const titleRef = useRef<HTMLInputElement>(null);
  const titleId = useId();
  const [title, setTitle] = useState("");
  const [statusId, setStatusId] = useState("");
  const [assigneeUserId, setAssigneeUserId] = useState("");
  const [dueDate, setDueDate] = useState("");
  const [spaceId, setSpaceId] = useState("");
  const [pickedListId, setPickedListId] = useState("");
  const needsPicker = !listId;

  const spacesQuery = useQuery({
    queryKey: workKeys.spaces(),
    queryFn: listSpaces,
    enabled: needsPicker,
  });
  const effectiveSpaceId = spaceId || spacesQuery.data?.[0]?.id || "";
  const listsQuery = useQuery({
    queryKey: workKeys.lists(effectiveSpaceId),
    queryFn: () => listLists(effectiveSpaceId),
    enabled: needsPicker && Boolean(effectiveSpaceId),
  });
  const effectiveListId = listId ?? (pickedListId || listsQuery.data?.[0]?.id || "");
  // Only needed in picker mode; with a fixed list the caller already has the statuses loaded.
  const schemesQuery = useQuery({
    queryKey: workKeys.statusSchemes(),
    queryFn: listStatusSchemes,
    enabled: needsPicker,
  });
  const pickedList = listsQuery.data?.find((entry) => entry.id === effectiveListId);
  const pickedStatuses =
    schemesQuery.data?.find((scheme) => scheme.id === pickedList?.statusSchemeId)?.statuses ??
    schemesQuery.data?.[0]?.statuses ??
    [];
  const availableStatuses = sortStatuses(statuses ?? pickedStatuses);
  const effectiveStatusId = statusId || availableStatuses[0]?.id || "";
  const members = useMembers().data ?? [];

  const create = useWorkMutation(createTask);

  useFocusTrap({ open: true, containerRef: dialogRef, onClose, initialFocusRef: titleRef });

  const trimmed = title.trim();
  const canSubmit = Boolean(trimmed) && Boolean(effectiveListId) && !create.isPending;

  function submit() {
    if (!canSubmit) {
      return;
    }

    create.mutate(
      {
        listId: effectiveListId,
        title: trimmed,
        parentId,
        statusId: effectiveStatusId || undefined,
        assigneeUserIds: assigneeUserId ? [assigneeUserId] : undefined,
        dueDate: dueDate || undefined,
      },
      {
        onSuccess: (task) => {
          onCreated?.(task.id);
          onClose();
        },
      },
    );
  }

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center px-4 py-24" role="presentation">
      <button
        type="button"
        aria-label="Close new task dialog"
        className="absolute inset-0 cursor-default bg-slate-950/40 backdrop-blur-[1px] pv-animate-backdrop"
        onClick={onClose}
      />
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        className="relative w-full max-w-lg overflow-hidden rounded-[var(--radius)] border border-border bg-card shadow-2xl outline-none pv-animate-command"
      >
        <form
          className="space-y-4 p-5"
          onSubmit={(event) => {
            event.preventDefault();
            submit();
          }}
        >
          <h2 id={titleId} className="text-lg font-semibold">
            New task
          </h2>
          {create.isError ? (
            <p
              role="alert"
              className="rounded-lg border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
            >
              This task could not be created: {(create.error as Error).message}
            </p>
          ) : null}
          <label className="grid gap-1 text-xs font-medium text-muted-foreground">
            Task name
            <input
              ref={titleRef}
              value={title}
              required
              placeholder="What needs doing?"
              className={fieldClassName}
              onChange={(event) => setTitle(event.currentTarget.value)}
            />
          </label>
          {needsPicker ? (
            <div className="grid gap-3 sm:grid-cols-2">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Space
                <select
                  value={effectiveSpaceId}
                  className={fieldClassName}
                  onChange={(event) => {
                    setSpaceId(event.target.value);
                    setPickedListId("");
                  }}
                >
                  {(spacesQuery.data ?? []).map((space) => (
                    <option key={space.id} value={space.id}>
                      {space.name}
                    </option>
                  ))}
                </select>
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                List
                <select
                  value={effectiveListId}
                  className={fieldClassName}
                  onChange={(event) => setPickedListId(event.target.value)}
                >
                  {(listsQuery.data ?? []).map((entry) => (
                    <option key={entry.id} value={entry.id}>
                      {entry.name}
                    </option>
                  ))}
                </select>
              </label>
            </div>
          ) : null}
          <div className="grid gap-3 sm:grid-cols-3">
            <label className="grid gap-1 text-xs font-medium text-muted-foreground">
              Status
              <select
                value={effectiveStatusId}
                disabled={availableStatuses.length === 0}
                className={fieldClassName}
                onChange={(event) => setStatusId(event.target.value)}
              >
                {availableStatuses.map((status) => (
                  <option key={status.id} value={status.id}>
                    {status.name}
                  </option>
                ))}
              </select>
            </label>
            <label className="grid gap-1 text-xs font-medium text-muted-foreground">
              Assignee
              <select
                value={assigneeUserId}
                className={fieldClassName}
                onChange={(event) => setAssigneeUserId(event.target.value)}
              >
                <option value="">Unassigned</option>
                {members.map((member) => (
                  <option key={member.userId} value={member.userId}>
                    {member.displayName || member.email || member.userId}
                  </option>
                ))}
              </select>
            </label>
            <label className="grid gap-1 text-xs font-medium text-muted-foreground">
              Due date
              <input
                type="date"
                value={dueDate}
                className={fieldClassName}
                onChange={(event) => setDueDate(event.currentTarget.value)}
              />
            </label>
          </div>
          <div className="flex justify-end gap-2 border-t border-border pt-4">
            <Button type="button" variant="ghost" size="sm" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" size="sm" disabled={!canSubmit}>
              {create.isPending ? "Creating…" : "Create task"}
            </Button>
          </div>
        </form>
      </div>
    </div>
  );
}
