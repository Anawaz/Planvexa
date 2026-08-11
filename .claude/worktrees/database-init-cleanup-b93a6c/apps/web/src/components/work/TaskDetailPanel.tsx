"use client";

import { useQuery } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { CommentThread } from "@/components/collab/CommentThread";
import { ShareDialog } from "@/components/collab/ShareDialog";
import { TaskTimeSection } from "@/components/time/TaskTimeSection";
import { ResourceSharingDialog } from "@/components/work/ResourceSharingDialog";
import { Button } from "@/components/ui/Button";
import { useRecordRecentView } from "@/lib/recent/useRecordRecentView";
import { useCurrentUserId, useMemberDirectory, useMembers, useTeams } from "@/lib/members";
import {
  addAssignee,
  addChecklist,
  addChecklistItem,
  addDependency,
  addTaskRelation,
  addTaskToList,
  addTeamAssignee,
  addWatcher,
  attachmentDownloadHref,
  copyTaskToList,
  createTag,
  createReminder,
  deleteAttachment,
  deleteReminder,
  deleteTask,
  duplicateTask,
  getTask,
  getTaskEstimate,
  listAttachments,
  listEffectiveCustomFields,
  listLists,
  listReminders,
  listTags,
  listTaskTypes,
  listTasks,
  mergeTask,
  removeAssignee,
  removeDependency,
  removeTaskFromList,
  removeTaskRelation,
  removeTeamAssignee,
  removeWatcher,
  restoreTask,
  setChecklistItemResolved,
  setCustomFieldRelationships,
  setCustomFieldValue,
  setTaskEstimate,
  setTaskTags,
  uploadAttachment,
} from "@/lib/work/client";
import { useTaskMutations, useWorkMutation } from "@/lib/work/mutations";
import { createTaskOffline as createTask } from "@/lib/work/offlineMutations";
import { workKeys } from "@/lib/work/queries";
import { setResourcePrivate } from "@/lib/work/sharing";
import type {
  DependencyType,
  Priority,
  StatusDefinition,
  UpdateTaskPatch,
} from "@/lib/work/types";
import {
  customFieldEditor,
  customFieldInputValue,
  findStatus,
  formatDate,
  priorityClassName,
  shortId,
  statusBadgeStyle,
  tagLabel,
} from "./helpers";
import { InlineComposer } from "./InlineComposer";
import { useFocusTrap } from "./useFocusTrap";

const priorities: Priority[] = ["None", "Low", "Normal", "High", "Urgent"];
const dependencyTypes: DependencyType[] = ["BlockedBy", "WaitingOn", "Blocks"];
const selectClassName =
  "h-9 rounded-lg border border-border bg-background px-2 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";
const fileSize = new Intl.NumberFormat("en", {
  notation: "compact",
  style: "unit",
  unit: "byte",
  unitDisplay: "narrow",
});

type TaskDetailPanelProps = {
  taskId: string | null;
  open: boolean;
  statuses: StatusDefinition[];
  onOpenTask?: (taskId: string) => void;
  onClose: () => void;
};

export function TaskDetailPanel({
  taskId,
  open,
  statuses,
  onOpenTask,
  onClose,
}: TaskDetailPanelProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const { getLabel, getInitials } = useMemberDirectory();
  useRecordRecentView("task", taskId, open);
  const [shareOpen, setShareOpen] = useState(false);
  const [aclSharingOpen, setAclSharingOpen] = useState(false);
  const togglePrivate = useWorkMutation((isPrivate: boolean) =>
    taskId ? setResourcePrivate("task", taskId, isPrivate) : Promise.resolve(undefined),
  );
  // Keyed by task id so switching tasks in the drawer never leaves a stale "confirm delete" armed.
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);
  const confirmingDelete = Boolean(taskId) && confirmDeleteId === taskId;
  const [deletedTaskId, setDeletedTaskId] = useState<string | null>(null);
  const [dependencyType, setDependencyType] = useState<DependencyType>("BlockedBy");
  const { updateTask } = useTaskMutations(statuses);
  const taskQuery = useQuery({
    queryKey: workKeys.task(taskId ?? "pending"),
    queryFn: () => getTask(taskId ?? ""),
    enabled: open && Boolean(taskId),
  });
  const tagsQuery = useQuery({
    queryKey: workKeys.tags(),
    queryFn: listTags,
    enabled: open,
  });
  const task = taskQuery.data;
  const members = useMembers().data ?? [];
  const currentUserId = useCurrentUserId();
  // ponytail: subtasks come from the list's task query (usually already cached) and are filtered here;
  // add a /tasks/{id}/children endpoint if lists ever get big enough for that to hurt.
  const listTasksQuery = useQuery({
    queryKey: workKeys.tasks(task?.listId ?? "pending", { sort: "position" }),
    queryFn: () => listTasks(task?.listId ?? "", { sort: "position" }),
    enabled: Boolean(task?.listId),
  });
  const subtasks = (listTasksQuery.data ?? []).filter((entry) => entry.parentId === task?.id);
  const assignMember = useWorkMutation((input: { userId: string; assigned: boolean }) =>
    input.assigned
      ? addAssignee(taskId ?? "", input.userId)
      : removeAssignee(taskId ?? "", input.userId),
  );
  const saveTags = useWorkMutation((tagIds: string[]) => setTaskTags(taskId ?? "", tagIds));
  const addNewTag = useWorkMutation(async (name: string) => {
    const tag = await createTag(name);
    await setTaskTags(taskId ?? "", [...(task?.tagIds ?? []), tag.id]);
  });
  const toggleWatch = useWorkMutation((input: { userId: string; watching: boolean }) =>
    input.watching
      ? addWatcher(taskId ?? "", input.userId)
      : removeWatcher(taskId ?? "", input.userId),
  );
  const createChecklist = useWorkMutation((name: string) => addChecklist(taskId ?? "", name));
  const createChecklistItem = useWorkMutation((input: { checklistId: string; content: string }) =>
    addChecklistItem(input.checklistId, input.content),
  );
  const toggleChecklistItem = useWorkMutation((input: { itemId: string; isResolved: boolean }) =>
    setChecklistItemResolved(input.itemId, input.isResolved),
  );
  const createSubtask = useWorkMutation(createTask);
  // The task's own list scope + Space/Workspace + any ancestor-Folder-inherited fields —
  // not every custom field in the workspace (which would show fields scoped to unrelated lists too).
  const customFieldsQuery = useQuery({
    queryKey: workKeys.listCustomFields(task?.listId ?? ""),
    queryFn: () => listEffectiveCustomFields(task!.listId),
    enabled: open && Boolean(task?.listId),
  });
  const saveCustomField = useWorkMutation((input: { definitionId: string; value: string | null }) =>
    setCustomFieldValue(taskId ?? "", input.definitionId, input.value),
  );
  const saveCustomFieldRelationships = useWorkMutation((input: { definitionId: string; relatedTaskIds: string[] }) =>
    setCustomFieldRelationships(taskId ?? "", input.definitionId, input.relatedTaskIds),
  );
  const saveDependency = useWorkMutation(
    (input: { dependsOnTaskId: string; type: DependencyType }) =>
      addDependency(taskId ?? "", input.dependsOnTaskId, input.type),
  );
  const dropDependency = useWorkMutation((dependencyId: string) =>
    removeDependency(taskId ?? "", dependencyId),
  );
  const removeTask = useWorkMutation(deleteTask);
  const undoDelete = useWorkMutation(restoreTask);
  const duplicate = useWorkMutation(duplicateTask);
  const attachmentsQuery = useQuery({
    queryKey: workKeys.attachments(taskId ?? "pending"),
    queryFn: () => listAttachments(taskId ?? ""),
    enabled: open && Boolean(taskId),
  });
  const addAttachment = useWorkMutation((file: File) => uploadAttachment(taskId ?? "", file));
  const removeAttachment = useWorkMutation(deleteAttachment);
  const remindersQuery = useQuery({
    queryKey: workKeys.reminders(taskId ?? "pending"),
    queryFn: () => listReminders(taskId ?? ""),
    enabled: open && Boolean(taskId),
  });
  const addReminder = useWorkMutation((input: { remindAtUtc: string; note?: string }) =>
    createReminder(taskId ?? "", input.remindAtUtc, input.note),
  );
  const removeReminder = useWorkMutation(deleteReminder);
  const [reminderAt, setReminderAt] = useState("");
  const [reminderNote, setReminderNote] = useState("");

  // ---- task management completeness ----
  const taskTypesQuery = useQuery({
    queryKey: workKeys.taskTypes(),
    queryFn: listTaskTypes,
    enabled: open,
  });
  const siblingListsQuery = useQuery({
    queryKey: workKeys.lists(task?.spaceId ?? "pending"),
    queryFn: () => listLists(task?.spaceId ?? ""),
    enabled: Boolean(task?.spaceId),
  });
  const teamsQuery = useTeams();
  const estimateQuery = useQuery({
    queryKey: workKeys.estimate(taskId ?? "pending"),
    queryFn: () => getTaskEstimate(taskId ?? ""),
    enabled: open && Boolean(taskId),
  });
  const saveEstimate = useWorkMutation((minutes: number | null) =>
    setTaskEstimate(taskId ?? "", Math.max(0, Math.round((minutes ?? 0) * 60))),
  );
  const addToList = useWorkMutation((listId: string) => addTaskToList(taskId ?? "", listId));
  const removeFromList = useWorkMutation((listId: string) => removeTaskFromList(taskId ?? "", listId));
  const copyToList = useWorkMutation((listId: string) => copyTaskToList(taskId ?? "", listId));
  const mergeInto = useWorkMutation((targetTaskId: string) => mergeTask(taskId ?? "", targetTaskId));
  const linkRelation = useWorkMutation((relatedTaskId: string) =>
    addTaskRelation(taskId ?? "", relatedTaskId),
  );
  const unlinkRelation = useWorkMutation((relatedTaskId: string) =>
    removeTaskRelation(taskId ?? "", relatedTaskId),
  );
  const assignTeam = useWorkMutation((input: { teamId: string; assigned: boolean }) =>
    input.assigned
      ? addTeamAssignee(taskId ?? "", input.teamId)
      : removeTeamAssignee(taskId ?? "", input.teamId),
  );

  useFocusTrap({ open, containerRef: panelRef, onClose, paused: shareOpen });

  // The panel closes on delete, so the undo toast has to outlive it.
  if (!open) {
    return deletedTaskId ? (
      <div
        role="status"
        className="fixed bottom-6 left-1/2 z-50 flex -translate-x-1/2 items-center gap-3 rounded-[var(--radius)] border border-border bg-card px-4 py-3 text-sm shadow-lg"
      >
        <span>Task deleted.</span>
        <Button
          type="button"
          size="sm"
          variant="secondary"
          disabled={undoDelete.isPending}
          onClick={() =>
            undoDelete.mutate(deletedTaskId, { onSuccess: () => setDeletedTaskId(null) })
          }
        >
          Undo
        </Button>
        <Button type="button" size="sm" variant="ghost" onClick={() => setDeletedTaskId(null)}>
          Dismiss
        </Button>
      </div>
    ) : null;
  }

  function closePanel() {
    setShareOpen(false);
    onClose();
  }

  function savePatch(patch: UpdateTaskPatch) {
    if (!taskId) {
      return;
    }

    updateTask.mutate({ taskId, patch });
  }

  const tagMap = new Map((tagsQuery.data ?? []).map((tag) => [tag.id, tag]));
  const taskStatus = task ? findStatus(statuses, task.statusId) : undefined;
  const isWatching = Boolean(currentUserId && task?.watcherUserIds.includes(currentUserId));
  // Dependency titles resolve from the list already in the cache; anything cross-list falls back
  // to a short id rather than firing one request per linked task.
  const taskTitleById = new Map(
    (listTasksQuery.data ?? []).map((entry) => [entry.id, entry.title]),
  );
  const customFieldDefinitions = customFieldsQuery.data ?? [];
  const customFieldValueMap = new Map(
    (task?.customFieldValues ?? []).map((value) => [value.definitionId, value]),
  );

  return (
    <div className="fixed inset-0 z-50" aria-labelledby="task-detail-title" role="presentation">
      <button
        type="button"
        aria-label="Close task details"
        className="absolute inset-0 cursor-default bg-slate-950/40 backdrop-blur-[1px] pv-animate-backdrop"
        onClick={closePanel}
      />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="task-detail-title"
        tabIndex={-1}
        className="absolute right-0 top-0 flex h-full w-full max-w-2xl flex-col overflow-hidden border-l border-border bg-card shadow-2xl outline-none sm:w-[42rem] pv-animate-drawer-right"
      >
        <header className="flex items-center justify-between border-b border-border px-5 py-4">
          <div>
            <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
              {task?.sequence ?? "Task"}
            </p>
            <h2 id="task-detail-title" className="text-lg font-semibold">
              Task details
            </h2>
          </div>
          <div className="flex items-center gap-2">
            <Button
              type="button"
              variant={isWatching ? "primary" : "outline"}
              size="sm"
              aria-pressed={isWatching}
              disabled={!task || !currentUserId || toggleWatch.isPending}
              onClick={() =>
                currentUserId &&
                toggleWatch.mutate({ userId: currentUserId, watching: !isWatching })
              }
            >
              {isWatching ? "Watching" : "Watch"}
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={!task}
              onClick={() => setShareOpen(true)}
            >
              Share
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={!task}
              onClick={() => setAclSharingOpen(true)}
            >
              Sharing…
            </Button>
            <Button
              type="button"
              variant={task?.isPrivate ? "primary" : "outline"}
              size="sm"
              aria-pressed={task?.isPrivate}
              disabled={!task || togglePrivate.isPending}
              onClick={() => task && togglePrivate.mutate(!task.isPrivate)}
            >
              {task?.isPrivate ? "Private" : "Make private"}
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={!task || duplicate.isPending}
              onClick={() => taskId && duplicate.mutate(taskId)}
            >
              {duplicate.isPending ? "Duplicating…" : "Duplicate"}
            </Button>
            {confirmingDelete ? (
              <>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="border-red-300 text-red-700 dark:border-red-900 dark:text-red-400"
                  disabled={removeTask.isPending}
                  onClick={() =>
                    taskId &&
                    removeTask.mutate(taskId, {
                      onSuccess: () => {
                        setDeletedTaskId(taskId);
                        setConfirmDeleteId(null);
                        closePanel();
                      },
                    })
                  }
                >
                  Confirm delete
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => setConfirmDeleteId(null)}
                >
                  Cancel
                </Button>
              </>
            ) : (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="text-red-600 hover:text-red-700 dark:text-red-400"
                disabled={!task}
                onClick={() => setConfirmDeleteId(taskId)}
              >
                Delete
              </Button>
            )}
            <Button type="button" variant="ghost" size="sm" onClick={closePanel}>
              Close
            </Button>
          </div>
        </header>
        {/* `useTaskMutations` rolls the optimistic edit back on failure but says nothing, so a
            rename that never reached the API just silently reverted. Outside the query branches
            below so it survives the panel dropping into its "unable to load" state. */}
        {updateTask.isError ? (
          <p
            role="alert"
            className="border-b border-red-300 bg-red-50 px-5 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
          >
            This change could not be saved: {(updateTask.error as Error).message}
          </p>
        ) : null}
        {taskQuery.isLoading ? (
          <div className="p-5 text-sm text-muted-foreground">Loading task…</div>
        ) : taskQuery.isError || !task ? (
          <div className="p-5 text-sm text-red-600 dark:text-red-400">
            Unable to load this task.
          </div>
        ) : (
          <div className="flex-1 overflow-y-auto px-5 py-5">
            <div className="space-y-5">
              <div className="grid gap-2">
                <label htmlFor="task-title" className="text-sm font-medium">
                  Title
                </label>
                <input
                  id="task-title"
                  key={`${task.id}-title-${task.title}`}
                  defaultValue={task.title}
                  className="rounded-lg border border-border bg-background px-3 py-2 text-xl font-semibold outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                  onBlur={(event) => {
                    const title = event.currentTarget.value.trim();

                    if (title && title !== task.title) {
                      savePatch({ title });
                    }
                  }}
                />
              </div>
              <div className="grid gap-2">
                <label htmlFor="task-description" className="text-sm font-medium">
                  Description
                </label>
                <textarea
                  id="task-description"
                  key={`${task.id}-description-${task.description ?? ""}`}
                  defaultValue={task.description ?? ""}
                  rows={5}
                  className="resize-y rounded-lg border border-border bg-background px-3 py-2 text-sm leading-6 outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                  onBlur={(event) => {
                    if (event.currentTarget.value !== (task.description ?? "")) {
                      savePatch({ description: event.currentTarget.value });
                    }
                  }}
                />
              </div>
              <div className="grid gap-4 sm:grid-cols-2">
                <label className="grid gap-2 text-sm font-medium">
                  Status
                  <select
                    value={task.statusId}
                    className="rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                    onChange={(event) => {
                      savePatch({ statusId: event.target.value });
                    }}
                  >
                    {statuses.map((status) => (
                      <option key={status.id} value={status.id}>
                        {status.name}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="grid gap-2 text-sm font-medium">
                  Priority
                  <select
                    value={task.priority}
                    className="rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                    onChange={(event) => {
                      savePatch({ priority: event.target.value as Priority });
                    }}
                  >
                    {priorities.map((priority) => (
                      <option key={priority} value={priority}>
                        {priority}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="grid gap-2 text-sm font-medium">
                  Start date
                  <input
                    type="date"
                    key={`${task.id}-start-${task.startDate ?? ""}`}
                    defaultValue={task.startDate ?? ""}
                    className="rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                    onBlur={(event) =>
                      savePatch({ startDate: event.currentTarget.value || undefined })
                    }
                  />
                </label>
                <label className="grid gap-2 text-sm font-medium">
                  Due date
                  <input
                    type="date"
                    key={`${task.id}-due-${task.dueDate ?? ""}`}
                    defaultValue={task.dueDate ?? ""}
                    className="rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                    onBlur={(event) =>
                      savePatch({ dueDate: event.currentTarget.value || undefined })
                    }
                  />
                </label>
              </div>
              <label className="inline-flex items-center gap-2 text-sm font-medium">
                <input
                  type="checkbox"
                  className="size-4 rounded border-border accent-[var(--primary)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                  checked={task.isMilestone}
                  disabled={updateTask.isPending}
                  onChange={(event) => savePatch({ isMilestone: event.currentTarget.checked })}
                />
                Milestone
              </label>
              <div className="grid gap-4 sm:grid-cols-3">
                <label className="grid gap-2 text-sm font-medium">
                  Type
                  <select
                    value={task.taskTypeId ?? ""}
                    className={selectClassName}
                    onChange={(event) => savePatch({ taskTypeId: event.target.value || undefined })}
                  >
                    <option value="">Task (default)</option>
                    {(taskTypesQuery.data ?? [])
                      .filter((type) => !type.isBuiltIn)
                      .map((type) => (
                        <option key={type.id} value={type.id}>
                          {type.name}
                        </option>
                      ))}
                  </select>
                </label>
                <label className="grid gap-2 text-sm font-medium">
                  Custom ID
                  <input
                    key={`${task.id}-custom-id-${task.customId ?? ""}`}
                    defaultValue={task.customId ?? ""}
                    placeholder="e.g. BUG-1"
                    className={selectClassName}
                    onBlur={(event) => {
                      const next = event.currentTarget.value.trim();
                      if (next && next !== (task.customId ?? "")) {
                        savePatch({ customId: next });
                      }
                    }}
                  />
                </label>
                <label className="grid gap-2 text-sm font-medium">
                  Estimate (minutes)
                  <input
                    type="number"
                    min={0}
                    key={`${task.id}-estimate-${estimateQuery.data?.estimateSeconds ?? 0}`}
                    defaultValue={
                      estimateQuery.data ? Math.round(estimateQuery.data.estimateSeconds / 60) : ""
                    }
                    disabled={saveEstimate.isPending}
                    className={selectClassName}
                    onBlur={(event) => {
                      const raw = event.currentTarget.value;
                      saveEstimate.mutate(raw === "" ? null : Number(raw));
                    }}
                  />
                </label>
              </div>
              <div className="flex flex-wrap gap-2 text-xs">
                <span
                  className="rounded-full border px-2 py-0.5 font-medium"
                  style={statusBadgeStyle(taskStatus)}
                >
                  {taskStatus?.name ?? "Unknown"}
                </span>
                <span className={priorityClassName(task.priority)}>{task.priority}</span>
                <span className="rounded-full bg-muted px-2 py-0.5 text-muted-foreground">
                  Due {formatDate(task.dueDate)}
                </span>
              </div>
              <section aria-labelledby="detail-subtasks" className="space-y-2">
                <h3 id="detail-subtasks" className="text-sm font-semibold">
                  Subtasks{subtasks.length > 0 ? ` · ${subtasks.length}` : ""}
                </h3>
                {subtasks.length > 0 ? (
                  <ul className="space-y-1">
                    {subtasks.map((subtask) => (
                      <li key={subtask.id}>
                        <button
                          type="button"
                          className="w-full truncate rounded-lg border border-border px-3 py-2 text-left text-sm hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                          onClick={() => onOpenTask?.(subtask.id)}
                        >
                          <span
                            className={subtask.isCompleted ? "line-through opacity-70" : undefined}
                          >
                            {subtask.title}
                          </span>
                        </button>
                      </li>
                    ))}
                  </ul>
                ) : null}
                <InlineComposer
                  label="Break this down — add a subtask"
                  pending={createSubtask.isPending}
                  onSubmit={(title) =>
                    createSubtask.mutate({ listId: task.listId, parentId: task.id, title })
                  }
                />
              </section>
              <section aria-labelledby="detail-lists" className="space-y-2">
                <h3 id="detail-lists" className="text-sm font-semibold">
                  Lists
                </h3>
                <div className="flex flex-wrap gap-2">
                  {(task.lists ?? []).map((membership) => {
                    const list = (siblingListsQuery.data ?? []).find((l) => l.id === membership.listId);
                    return (
                      <span
                        key={membership.listId}
                        className="inline-flex items-center gap-2 rounded-full border border-border bg-background px-2 py-1 text-xs"
                      >
                        {list?.name ?? shortId(membership.listId)}
                        {membership.isPrimary ? (
                          <span className="text-muted-foreground">(primary)</span>
                        ) : (
                          <button
                            type="button"
                            aria-label={`Remove from ${list?.name ?? membership.listId}`}
                            disabled={removeFromList.isPending}
                            className="rounded px-1 text-muted-foreground hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                            onClick={() => removeFromList.mutate(membership.listId)}
                          >
                            ×
                          </button>
                        )}
                      </span>
                    );
                  })}
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <select
                    aria-label="Add to another list"
                    value=""
                    disabled={addToList.isPending}
                    className={selectClassName}
                    onChange={(event) => {
                      if (event.target.value) addToList.mutate(event.target.value);
                    }}
                  >
                    <option value="">Add to another list…</option>
                    {(siblingListsQuery.data ?? [])
                      .filter((l) => !(task.lists ?? []).some((m) => m.listId === l.id))
                      .map((l) => (
                        <option key={l.id} value={l.id}>
                          {l.name}
                        </option>
                      ))}
                  </select>
                  <select
                    aria-label="Copy to another list"
                    value=""
                    disabled={copyToList.isPending}
                    className={selectClassName}
                    onChange={(event) => {
                      if (event.target.value) copyToList.mutate(event.target.value);
                    }}
                  >
                    <option value="">Copy to list…</option>
                    {(siblingListsQuery.data ?? []).map((l) => (
                      <option key={l.id} value={l.id}>
                        {l.name}
                      </option>
                    ))}
                  </select>
                </div>
              </section>
              <section aria-labelledby="detail-assignees" className="space-y-2">
                <h3 id="detail-assignees" className="text-sm font-semibold">
                  Assignees
                </h3>
                <div className="flex flex-wrap gap-2">
                  {task.assigneeUserIds.map((userId) => (
                    <span
                      key={userId}
                      className="inline-flex items-center gap-2 rounded-full border border-border bg-background px-2 py-1 text-xs"
                    >
                      <span className="grid size-6 place-items-center rounded-full bg-muted font-semibold">
                        {getInitials(userId)}
                      </span>
                      {getLabel(userId)}
                      <button
                        type="button"
                        aria-label={`Remove ${getLabel(userId)}`}
                        disabled={assignMember.isPending}
                        className="rounded px-1 text-muted-foreground hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                        onClick={() => assignMember.mutate({ userId, assigned: false })}
                      >
                        ×
                      </button>
                    </span>
                  ))}
                </div>
                <select
                  aria-label="Add assignee"
                  value=""
                  disabled={assignMember.isPending}
                  className="h-9 rounded-lg border border-border bg-background px-2 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                  onChange={(event) => {
                    if (event.target.value) {
                      assignMember.mutate({ userId: event.target.value, assigned: true });
                    }
                  }}
                >
                  <option value="">Add assignee…</option>
                  {members
                    .filter((member) => !task.assigneeUserIds.includes(member.userId))
                    .map((member) => (
                      <option key={member.userId} value={member.userId}>
                        {member.displayName || member.email || member.userId}
                      </option>
                    ))}
                </select>
                {/* Team assignees, shown as the team itself (not expanded to its members). */}
                <div className="flex flex-wrap gap-2 pt-1">
                  {task.teamAssigneeIds.map((teamId) => {
                    const team = (teamsQuery.data ?? []).find((t) => t.id === teamId);
                    return (
                      <span
                        key={teamId}
                        className="inline-flex items-center gap-2 rounded-full border border-dashed border-border bg-background px-2 py-1 text-xs"
                      >
                        {team?.name ?? shortId(teamId)}
                        <button
                          type="button"
                          aria-label={`Remove team ${team?.name ?? teamId}`}
                          disabled={assignTeam.isPending}
                          className="rounded px-1 text-muted-foreground hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                          onClick={() => assignTeam.mutate({ teamId, assigned: false })}
                        >
                          ×
                        </button>
                      </span>
                    );
                  })}
                </div>
                <select
                  aria-label="Add team"
                  value=""
                  disabled={assignTeam.isPending}
                  className={selectClassName}
                  onChange={(event) => {
                    if (event.target.value) {
                      assignTeam.mutate({ teamId: event.target.value, assigned: true });
                    }
                  }}
                >
                  <option value="">Add team…</option>
                  {(teamsQuery.data ?? [])
                    .filter((team) => !task.teamAssigneeIds.includes(team.id))
                    .map((team) => (
                      <option key={team.id} value={team.id}>
                        {team.name}
                      </option>
                    ))}
                </select>
              </section>
              <section aria-labelledby="detail-tags" className="space-y-2">
                <h3 id="detail-tags" className="text-sm font-semibold">
                  Tags
                </h3>
                <div className="flex flex-wrap gap-2">
                  {task.tagIds.map((tagId) => {
                    const tag = tagMap.get(tagId);

                    return (
                      <span
                        key={tagId}
                        className="inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-medium"
                        style={{
                          borderColor: `${tag?.color ?? "#64748b"}55`,
                          color: tag?.color ?? "currentColor",
                        }}
                      >
                        {tag?.name ?? tagLabel(tagId)}
                        <button
                          type="button"
                          aria-label={`Remove tag ${tag?.name ?? tagLabel(tagId)}`}
                          disabled={saveTags.isPending}
                          className="rounded px-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                          onClick={() =>
                            saveTags.mutate(task.tagIds.filter((current) => current !== tagId))
                          }
                        >
                          ×
                        </button>
                      </span>
                    );
                  })}
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <select
                    aria-label="Add tag"
                    value=""
                    disabled={saveTags.isPending}
                    className="h-9 rounded-lg border border-border bg-background px-2 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                    onChange={(event) => {
                      if (event.target.value) {
                        saveTags.mutate([...task.tagIds, event.target.value]);
                      }
                    }}
                  >
                    <option value="">Add tag…</option>
                    {(tagsQuery.data ?? [])
                      .filter((tag) => !task.tagIds.includes(tag.id))
                      .map((tag) => (
                        <option key={tag.id} value={tag.id}>
                          {tag.name}
                        </option>
                      ))}
                  </select>
                  <InlineComposer
                    label="New tag"
                    submitLabel="Create"
                    pending={addNewTag.isPending}
                    onSubmit={(name) => addNewTag.mutate(name)}
                  />
                </div>
              </section>
              <TaskTimeSection taskId={task.id} taskTitle={task.title} />
              <section aria-labelledby="detail-checklists" className="space-y-3">
                <h3 id="detail-checklists" className="text-sm font-semibold">
                  Checklists
                </h3>
                {task.checklists.map((checklist) => (
                  <div key={checklist.id} className="rounded-xl border border-border p-3">
                    <h4 className="text-sm font-medium">{checklist.title}</h4>
                    <ul className="mt-2 space-y-2">
                      {checklist.items.map((item) => (
                        <li key={item.id} className="flex items-center gap-2 text-sm">
                          <input
                            type="checkbox"
                            checked={item.isCompleted}
                            disabled={toggleChecklistItem.isPending}
                            className="size-4 accent-[var(--primary)]"
                            aria-label={item.title}
                            onChange={(event) =>
                              toggleChecklistItem.mutate({
                                itemId: item.id,
                                isResolved: event.currentTarget.checked,
                              })
                            }
                          />
                          <span className={item.isCompleted ? "line-through opacity-70" : undefined}>
                            {item.title}
                          </span>
                        </li>
                      ))}
                    </ul>
                    <InlineComposer
                      label="New item"
                      className="mt-3"
                      pending={createChecklistItem.isPending}
                      onSubmit={(content) =>
                        createChecklistItem.mutate({ checklistId: checklist.id, content })
                      }
                    />
                  </div>
                ))}
                <InlineComposer
                  label="New checklist"
                  pending={createChecklist.isPending}
                  onSubmit={(name) => createChecklist.mutate(name)}
                />
              </section>
              <section aria-labelledby="detail-attachments" className="space-y-2">
                <h3 id="detail-attachments" className="text-sm font-semibold">
                  Attachments
                </h3>
                {(attachmentsQuery.data ?? []).length === 0 ? (
                  <p className="text-sm text-muted-foreground">No attachments yet.</p>
                ) : (
                  <ul className="space-y-1">
                    {(attachmentsQuery.data ?? []).map((attachment) => (
                      <li
                        key={attachment.id}
                        className="flex items-center justify-between gap-3 rounded-lg border border-border px-3 py-2 text-sm"
                      >
                        <a
                          href={attachmentDownloadHref(attachment.id)}
                          download={attachment.fileName}
                          className="truncate font-medium underline-offset-2 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                        >
                          {attachment.fileName}
                        </a>
                        <span className="shrink-0 text-xs text-muted-foreground">
                          {fileSize.format(attachment.sizeBytes)} ·{" "}
                          {getLabel(attachment.uploadedByUserId)} ·{" "}
                          {new Intl.DateTimeFormat("en", {
                            month: "short",
                            day: "numeric",
                          }).format(new Date(attachment.createdAtUtc))}
                        </span>
                        <button
                          type="button"
                          aria-label={`Delete ${attachment.fileName}`}
                          disabled={removeAttachment.isPending}
                          className="shrink-0 rounded px-1 text-muted-foreground hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                          onClick={() => removeAttachment.mutate(attachment.id)}
                        >
                          ×
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
                <input
                  type="file"
                  aria-label="Upload attachment"
                  disabled={addAttachment.isPending}
                  className="block w-full text-sm text-muted-foreground file:mr-3 file:rounded-lg file:border file:border-border file:bg-background file:px-3 file:py-1.5 file:text-sm file:font-medium file:text-foreground"
                  onChange={(event) => {
                    const file = event.currentTarget.files?.[0];

                    if (file) {
                      addAttachment.mutate(file);
                    }

                    // Allow re-picking the same file after an upload or a failure.
                    event.currentTarget.value = "";
                  }}
                />
                {addAttachment.isError ? (
                  <p className="text-sm text-red-600 dark:text-red-400">
                    {(addAttachment.error as Error).message}
                  </p>
                ) : null}
              </section>
              <section aria-labelledby="detail-reminders" className="space-y-2">
                <h3 id="detail-reminders" className="text-sm font-semibold">
                  Reminders
                </h3>
                {(remindersQuery.data ?? []).length === 0 ? (
                  <p className="text-sm text-muted-foreground">No reminders set.</p>
                ) : (
                  <ul className="space-y-1">
                    {(remindersQuery.data ?? []).map((reminder) => (
                      <li
                        key={reminder.id}
                        className="flex items-center justify-between gap-3 rounded-lg border border-border px-3 py-2 text-sm"
                      >
                        <span className="min-w-0 truncate">
                          {new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" }).format(new Date(reminder.remindAtUtc))}
                          {reminder.note ? <span className="text-muted-foreground"> · {reminder.note}</span> : null}
                          {reminder.isSent ? <span className="ml-2 text-xs text-muted-foreground">(sent)</span> : null}
                        </span>
                        <button
                          type="button"
                          aria-label="Delete reminder"
                          disabled={removeReminder.isPending}
                          className="shrink-0 rounded px-1 text-muted-foreground hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                          onClick={() => removeReminder.mutate(reminder.id)}
                        >
                          ×
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
                <div className="flex flex-wrap items-end gap-2">
                  <label className="grid gap-1 text-xs font-medium">
                    Remind at
                    <input
                      type="datetime-local"
                      value={reminderAt}
                      onChange={(event) => setReminderAt(event.target.value)}
                      className="h-9 rounded-lg border border-border bg-background px-2 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                    />
                  </label>
                  <label className="grid flex-1 gap-1 text-xs font-medium">
                    Note
                    <input
                      value={reminderNote}
                      onChange={(event) => setReminderNote(event.target.value)}
                      placeholder="Optional"
                      className="h-9 rounded-lg border border-border bg-background px-2 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                    />
                  </label>
                  <Button
                    type="button"
                    size="sm"
                    disabled={!reminderAt || addReminder.isPending}
                    onClick={() =>
                      addReminder.mutate(
                        { remindAtUtc: new Date(reminderAt).toISOString(), note: reminderNote || undefined },
                        { onSuccess: () => { setReminderAt(""); setReminderNote(""); } },
                      )
                    }
                  >
                    Add reminder
                  </Button>
                </div>
              </section>
              <section aria-labelledby="detail-metadata" className="grid gap-3 sm:grid-cols-2">
                <h3 id="detail-metadata" className="sr-only">
                  Metadata
                </h3>
                <div className="space-y-2 rounded-xl border border-border p-3">
                  <h4 className="text-sm font-semibold">Dependencies</h4>
                  {task.dependencies.length === 0 ? (
                    <p className="text-sm text-muted-foreground">No dependencies.</p>
                  ) : (
                    <ul className="space-y-2 text-sm">
                      {task.dependencies.map((dependency) => (
                        <li key={dependency.id} className="flex items-center justify-between gap-2">
                          <span className="min-w-0 truncate">
                            <span className="text-muted-foreground">{dependency.type}: </span>
                            {taskTitleById.get(dependency.dependsOnTaskId) ??
                              shortId(dependency.dependsOnTaskId)}
                          </span>
                          <button
                            type="button"
                            aria-label={`Remove ${dependency.type} dependency`}
                            disabled={dropDependency.isPending}
                            className="shrink-0 rounded px-1 text-muted-foreground hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                            onClick={() => dropDependency.mutate(dependency.id)}
                          >
                            ×
                          </button>
                        </li>
                      ))}
                    </ul>
                  )}
                  <div className="flex flex-wrap gap-2">
                    <select
                      aria-label="Dependency type"
                      value={dependencyType}
                      className={selectClassName}
                      onChange={(event) => setDependencyType(event.target.value as DependencyType)}
                    >
                      {dependencyTypes.map((type) => (
                        <option key={type} value={type}>
                          {type}
                        </option>
                      ))}
                    </select>
                    <select
                      aria-label="Add dependency"
                      value=""
                      disabled={saveDependency.isPending}
                      className={selectClassName}
                      onChange={(event) => {
                        if (event.target.value) {
                          saveDependency.mutate({
                            dependsOnTaskId: event.target.value,
                            type: dependencyType,
                          });
                        }
                      }}
                    >
                      <option value="">Link a task…</option>
                      {(listTasksQuery.data ?? [])
                        .filter(
                          (candidate) =>
                            candidate.id !== task.id &&
                            !task.dependencies.some(
                              (dependency) => dependency.dependsOnTaskId === candidate.id,
                            ),
                        )
                        .map((candidate) => (
                          <option key={candidate.id} value={candidate.id}>
                            {candidate.title}
                          </option>
                        ))}
                    </select>
                  </div>
                  {saveDependency.isError || dropDependency.isError ? (
                    <p role="alert" className="text-sm text-red-600 dark:text-red-400">
                      {((saveDependency.error ?? dropDependency.error) as Error).message}
                    </p>
                  ) : null}
                </div>
                <div className="space-y-2 rounded-xl border border-border p-3">
                  <h4 className="text-sm font-semibold">Custom fields</h4>
                  {customFieldDefinitions.length === 0 ? (
                    <p className="text-sm text-muted-foreground">
                      No custom fields defined for this workspace.
                    </p>
                  ) : (
                    <dl className="space-y-2 text-sm">
                      {customFieldDefinitions.map((definition) => {
                        const value = customFieldValueMap.get(definition.id);
                        const current = customFieldInputValue(definition, value);
                        const editor = customFieldEditor(definition.type);
                        const inputId = `custom-field-${definition.id}`;

                        return (
                          <div key={definition.id} className="grid gap-1">
                            <dt>
                              <label
                                htmlFor={inputId}
                                className="text-xs font-medium text-muted-foreground"
                              >
                                {definition.name}
                              </label>
                            </dt>
                            <dd>
                              {editor === "computed" ? (
                                // Formula/Rollup are computed server-side at read time —
                                // never directly editable. computedError surfaces an evaluation failure
                                // (unresolved dependency, division by zero, no source data) in place of a
                                // silently wrong zero.
                                <span
                                  className={
                                    value?.computedError
                                      ? "text-sm text-red-600 dark:text-red-400"
                                      : "text-sm text-muted-foreground"
                                  }
                                  title={definition.type === "Formula" ? (definition.formulaExpression ?? undefined) : undefined}
                                >
                                  {value?.computedError ?? (current || "—")}
                                </span>
                              ) : editor === "boolean" ? (
                                <input
                                  id={inputId}
                                  type="checkbox"
                                  className="size-4 rounded border-border accent-[var(--primary)]"
                                  checked={current === "true"}
                                  disabled={saveCustomField.isPending}
                                  onChange={(event) =>
                                    saveCustomField.mutate({
                                      definitionId: definition.id,
                                      value: String(event.currentTarget.checked),
                                    })
                                  }
                                />
                              ) : editor === "dropdown" ? (
                                <select
                                  id={inputId}
                                  value={current}
                                  disabled={saveCustomField.isPending}
                                  className={`${selectClassName} w-full`}
                                  onChange={(event) =>
                                    saveCustomField.mutate({
                                      definitionId: definition.id,
                                      value: event.target.value || null,
                                    })
                                  }
                                >
                                  <option value="">—</option>
                                  {definition.options.map((option) => (
                                    <option key={option.id} value={option.id}>
                                      {option.label}
                                    </option>
                                  ))}
                                </select>
                              ) : editor === "user" ? (
                                <select
                                  id={inputId}
                                  value={current}
                                  disabled={saveCustomField.isPending}
                                  className={`${selectClassName} w-full`}
                                  onChange={(event) =>
                                    saveCustomField.mutate({
                                      definitionId: definition.id,
                                      value: event.target.value || null,
                                    })
                                  }
                                >
                                  <option value="">—</option>
                                  {members.map((member) => (
                                    <option key={member.userId} value={member.userId}>
                                      {member.displayName || member.email || member.userId}
                                    </option>
                                  ))}
                                </select>
                              ) : editor === "team" ? (
                                <select
                                  id={inputId}
                                  value={current}
                                  disabled={saveCustomField.isPending}
                                  className={`${selectClassName} w-full`}
                                  onChange={(event) =>
                                    saveCustomField.mutate({
                                      definitionId: definition.id,
                                      value: event.target.value || null,
                                    })
                                  }
                                >
                                  <option value="">—</option>
                                  {(teamsQuery.data ?? []).map((team) => (
                                    <option key={team.id} value={team.id}>
                                      {team.name}
                                    </option>
                                  ))}
                                </select>
                              ) : editor === "relationship" ? (
                                (() => {
                                  const linkedIds = value?.relatedTaskIds ?? [];
                                  return (
                                    <div className="space-y-1">
                                      {linkedIds.length > 0 ? (
                                        <ul className="space-y-1">
                                          {linkedIds.map((relatedId) => (
                                            <li key={relatedId} className="flex items-center justify-between gap-2">
                                              <button
                                                type="button"
                                                className="min-w-0 truncate text-left underline-offset-2 hover:underline"
                                                onClick={() => onOpenTask?.(relatedId)}
                                              >
                                                {taskTitleById.get(relatedId) ?? shortId(relatedId)}
                                              </button>
                                              <button
                                                type="button"
                                                aria-label="Remove link"
                                                disabled={saveCustomFieldRelationships.isPending}
                                                className="shrink-0 rounded px-1 text-muted-foreground hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                                                onClick={() =>
                                                  saveCustomFieldRelationships.mutate({
                                                    definitionId: definition.id,
                                                    relatedTaskIds: linkedIds.filter((id) => id !== relatedId),
                                                  })
                                                }
                                              >
                                                ×
                                              </button>
                                            </li>
                                          ))}
                                        </ul>
                                      ) : null}
                                      <select
                                        aria-label={`Link a task for ${definition.name}`}
                                        value=""
                                        disabled={saveCustomFieldRelationships.isPending}
                                        className={`${selectClassName} w-full`}
                                        onChange={(event) => {
                                          if (event.target.value) {
                                            saveCustomFieldRelationships.mutate({
                                              definitionId: definition.id,
                                              relatedTaskIds: [...linkedIds, event.target.value],
                                            });
                                          }
                                        }}
                                      >
                                        <option value="">Link a task…</option>
                                        {(listTasksQuery.data ?? [])
                                          .filter((candidate) => candidate.id !== task.id && !linkedIds.includes(candidate.id))
                                          .map((candidate) => (
                                            <option key={candidate.id} value={candidate.id}>
                                              {candidate.title}
                                            </option>
                                          ))}
                                      </select>
                                    </div>
                                  );
                                })()
                              ) : (
                                <input
                                  id={inputId}
                                  key={`${task.id}-${definition.id}-${current}`}
                                  type={
                                    editor === "number"
                                      ? "number"
                                      : editor === "date"
                                        ? "date"
                                        : "text"
                                  }
                                  min={definition.type === "Progress" ? 0 : undefined}
                                  max={definition.type === "Progress" ? 100 : undefined}
                                  defaultValue={current}
                                  disabled={saveCustomField.isPending}
                                  className={`${selectClassName} w-full`}
                                  onBlur={(event) => {
                                    const next = event.currentTarget.value;

                                    if (next !== current) {
                                      saveCustomField.mutate({
                                        definitionId: definition.id,
                                        value: next || null,
                                      });
                                    }
                                  }}
                                />
                              )}
                            </dd>
                          </div>
                        );
                      })}
                    </dl>
                  )}
                  {saveCustomField.isError || saveCustomFieldRelationships.isError ? (
                    <p role="alert" className="text-sm text-red-600 dark:text-red-400">
                      {((saveCustomField.error ?? saveCustomFieldRelationships.error) as Error).message}
                    </p>
                  ) : null}
                </div>
              </section>
              <section aria-labelledby="detail-relations" className="space-y-2">
                <h3 id="detail-relations" className="text-sm font-semibold">
                  Linked tasks
                </h3>
                {(task.relations ?? []).length === 0 ? (
                  <p className="text-sm text-muted-foreground">No linked tasks.</p>
                ) : (
                  <ul className="space-y-2 text-sm">
                    {(task.relations ?? []).map((relation) => (
                      <li key={relation.relatedTaskId} className="flex items-center justify-between gap-2">
                        <button
                          type="button"
                          className="min-w-0 truncate text-left underline-offset-2 hover:underline"
                          onClick={() => onOpenTask?.(relation.relatedTaskId)}
                        >
                          {taskTitleById.get(relation.relatedTaskId) ?? shortId(relation.relatedTaskId)}
                        </button>
                        <button
                          type="button"
                          aria-label="Remove link"
                          disabled={unlinkRelation.isPending}
                          className="shrink-0 rounded px-1 text-muted-foreground hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                          onClick={() => unlinkRelation.mutate(relation.relatedTaskId)}
                        >
                          ×
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
                <select
                  aria-label="Link a task"
                  value=""
                  disabled={linkRelation.isPending}
                  className={selectClassName}
                  onChange={(event) => {
                    if (event.target.value) linkRelation.mutate(event.target.value);
                  }}
                >
                  <option value="">Link a task…</option>
                  {(listTasksQuery.data ?? [])
                    .filter(
                      (candidate) =>
                        candidate.id !== task.id &&
                        !(task.relations ?? []).some((r) => r.relatedTaskId === candidate.id),
                    )
                    .map((candidate) => (
                      <option key={candidate.id} value={candidate.id}>
                        {candidate.title}
                      </option>
                    ))}
                </select>
                <div className="border-t border-border pt-2">
                  <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                    Merge this task into…
                    <select
                      aria-label="Merge this task into"
                      value=""
                      disabled={mergeInto.isPending}
                      className={selectClassName}
                      onChange={(event) => {
                        const targetId = event.target.value;
                        const targetTitle = (listTasksQuery.data ?? []).find((c) => c.id === targetId)?.title;
                        if (
                          targetId &&
                          window.confirm(
                            `Merge "${task.title}" into "${targetTitle ?? targetId}"? Checklists, attachments and custom-field values move to the target; this task is archived.`,
                          )
                        ) {
                          mergeInto.mutate(targetId, { onSuccess: closePanel });
                        }
                      }}
                    >
                      <option value="">Select a target task…</option>
                      {(listTasksQuery.data ?? [])
                        .filter((candidate) => candidate.id !== task.id)
                        .map((candidate) => (
                          <option key={candidate.id} value={candidate.id}>
                            {candidate.title}
                          </option>
                        ))}
                    </select>
                  </label>
                </div>
              </section>
              <section aria-labelledby="detail-activity" className="space-y-3">
                <h3 id="detail-activity" className="text-sm font-semibold">
                  Activity
                </h3>
                <ol className="space-y-3">
                  {task.activity.map((entry) => (
                    <li key={entry.id} className="rounded-xl border border-border p-3 text-sm">
                      <span className="font-medium">{getLabel(entry.actorUserId)}</span>{" "}
                      <span className="text-muted-foreground">{entry.action}</span>
                      <p className="mt-1 text-xs text-muted-foreground">
                        {new Intl.DateTimeFormat("en", {
                          month: "short",
                          day: "numeric",
                          hour: "numeric",
                          minute: "2-digit",
                        }).format(new Date(entry.createdAt))}
                      </p>
                    </li>
                  ))}
                </ol>
              </section>
              <CommentThread taskId={task.id} />
            </div>
            <ShareDialog taskId={task.id} open={shareOpen} onOpenChange={setShareOpen} />
            <ResourceSharingDialog
              resourceType="task"
              resourceId={task.id}
              resourceName={task.title}
              open={aclSharingOpen}
              onOpenChange={setAclSharingOpen}
            />
          </div>
        )}
      </div>
    </div>
  );
}
