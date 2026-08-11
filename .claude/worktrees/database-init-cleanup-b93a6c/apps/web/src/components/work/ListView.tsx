"use client";

import { Fragment, useState, type ReactNode } from "react";
import { useMemberDirectory } from "@/lib/members";
import { createTaskOffline as createTask } from "@/lib/work/offlineMutations";
import { useTaskMutations, useWorkMutation } from "@/lib/work/mutations";
import type { StatusDefinition, Task } from "@/lib/work/types";
import { cn } from "@/lib/utils";
import {
  buildTaskTree,
  dueDateClassName,
  findStatus,
  formatDate,
  groupTasksByStatus,
  priorityClassName,
  statusBadgeStyle,
} from "./helpers";
import { AddTaskButton } from "./AddTaskButton";
import { InlineComposer } from "./InlineComposer";
import type { TaskSelection } from "./selection";

type ListViewProps = {
  tasks: Task[];
  statuses: StatusDefinition[];
  listId: string;
  /** The list has no tasks at all — the page owns the zero-state, so skip the per-status one. */
  listIsEmpty?: boolean;
  selection: TaskSelection;
  onOpenTask: (taskId: string) => void;
};

export function ListView({
  tasks,
  statuses,
  listId,
  listIsEmpty = false,
  selection,
  onOpenTask,
}: ListViewProps) {
  const { completeTask, reopenTask } = useTaskMutations(statuses);
  const create = useWorkMutation(createTask);
  const { getLabel, getInitials } = useMemberDirectory();
  // Parents start expanded; collapsing is per-session only. ponytail: persist to localStorage if
  // anyone actually asks for their tree state to survive a reload.
  const [collapsed, setCollapsed] = useState<Set<string>>(() => new Set());
  const [composerFor, setComposerFor] = useState<string | null>(null);

  // ponytail: children hang off their parent whatever their own status is, so a subtask never shows
  // up in its own status group. Split the tree per status when someone actually asks for it.
  const { childrenOf, roots } = buildTaskTree(tasks);
  const groups = groupTasksByStatus(roots, statuses);

  function expand(taskId: string) {
    setCollapsed((current) => {
      const next = new Set(current);
      next.delete(taskId);
      return next;
    });
  }

  function toggleCollapsed(taskId: string) {
    setCollapsed((current) => {
      const next = new Set(current);

      if (!next.delete(taskId)) {
        next.add(taskId);
      }

      return next;
    });
  }

  function renderRow(task: Task, depth: number): ReactNode {
    const status = findStatus(statuses, task.statusId);
    const toggleComplete = task.isCompleted ? reopenTask : completeTask;
    const children = childrenOf.get(task.id) ?? [];
    const isCollapsed = collapsed.has(task.id);

    return (
      <Fragment key={task.id}>
        <article
          style={{ paddingLeft: `${1 + depth * 1.5}rem` }}
          className="group grid gap-3 py-3 pr-4 transition hover:bg-muted/40 sm:grid-cols-[auto_auto_1fr_auto] sm:items-center"
        >
          {/* Two always-visible checkboxes per row read as one ambiguous pair, so selection stays
              hidden until you hover, focus it, or a selection is already in progress. */}
          <input
            type="checkbox"
            className={cn(
              "size-4 rounded border-border accent-[var(--primary)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring",
              "opacity-0 transition-opacity group-hover:opacity-100 focus-visible:opacity-100",
              selection.selectedIds.length > 0 && "opacity-100",
            )}
            checked={selection.isSelected(task.id)}
            aria-label={`Select ${task.title}`}
            onChange={() => selection.toggle(task.id)}
          />
          <input
            type="checkbox"
            className="size-4 rounded-full border-border accent-[var(--primary)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            checked={task.isCompleted}
            disabled={toggleComplete.isPending}
            aria-label={`${task.isCompleted ? "Reopen" : "Complete"} ${task.title}`}
            onChange={() => toggleComplete.mutate(task.id)}
          />
          <div className="min-w-0">
            <div className="flex min-w-0 items-center gap-1">
              {children.length > 0 ? (
                <button
                  type="button"
                  aria-expanded={!isCollapsed}
                  aria-label={`${isCollapsed ? "Expand" : "Collapse"} subtasks of ${task.title}`}
                  className="grid size-5 shrink-0 place-items-center rounded text-muted-foreground transition hover:bg-muted hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                  onClick={() => toggleCollapsed(task.id)}
                >
                  <span aria-hidden="true" className="text-xs">
                    {isCollapsed ? "▸" : "▾"}
                  </span>
                </button>
              ) : (
                <span aria-hidden="true" className="size-5 shrink-0" />
              )}
              <button
                type="button"
                className="min-w-0 flex-1 truncate text-left text-sm font-medium hover:text-primary focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                onClick={() => onOpenTask(task.id)}
              >
                <span className={task.isCompleted ? "line-through opacity-70" : undefined}>
                  {task.title}
                </span>
              </button>
              {children.length > 0 ? (
                <span className="shrink-0 rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
                  {children.length} subtask{children.length === 1 ? "" : "s"}
                </span>
              ) : null}
            </div>
            <div className="mt-2 flex flex-wrap items-center gap-2 pl-6 text-xs">
              <span
                className="rounded-full border px-2 py-0.5 font-medium"
                style={statusBadgeStyle(status)}
              >
                {status?.name ?? "Unknown"}
              </span>
              <span className={priorityClassName(task.priority)}>{task.priority}</span>
              <span className={dueDateClassName(task.dueDate, task.isCompleted)}>
                Due {formatDate(task.dueDate)}
              </span>
              {task.isMilestone ? (
                <span className="rounded-full bg-primary/10 px-2 py-0.5 font-medium text-primary">
                  Milestone
                </span>
              ) : null}
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-1 sm:justify-end">
            {task.assigneeUserIds.map((userId) => (
              <span
                key={userId}
                title={getLabel(userId)}
                className="grid size-8 place-items-center rounded-full border border-border bg-background text-xs font-semibold text-muted-foreground"
              >
                {getInitials(userId)}
              </span>
            ))}
            <button
              type="button"
              aria-label={`Add subtask to ${task.title}`}
              className={cn(
                "grid size-8 shrink-0 place-items-center rounded-full border border-border text-muted-foreground opacity-0 transition hover:border-primary hover:text-foreground focus-visible:opacity-100 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring group-hover:opacity-100",
                composerFor === task.id && "opacity-100",
              )}
              onClick={() => {
                setComposerFor((current) => (current === task.id ? null : task.id));
                expand(task.id);
              }}
            >
              <span aria-hidden="true">+</span>
            </button>
          </div>
        </article>
        {composerFor === task.id ? (
          <div className="py-2 pr-4" style={{ paddingLeft: `${1 + (depth + 1) * 1.5}rem` }}>
            <InlineComposer
              label={`New subtask of ${task.title}`}
              autoFocus
              pending={create.isPending}
              onCancel={() => setComposerFor(null)}
              onSubmit={(title) =>
                create.mutate(
                  { listId, title, parentId: task.id, statusId: task.statusId },
                  { onSuccess: () => setComposerFor(null) },
                )
              }
            />
          </div>
        ) : null}
        {isCollapsed ? null : children.map((child) => renderRow(child, depth + 1))}
      </Fragment>
    );
  }

  return (
    <div className="space-y-4">
      {create.isError ? (
        <p
          role="alert"
          className="rounded-[var(--radius)] border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
        >
          This task could not be created: {(create.error as Error).message}
        </p>
      ) : null}
      {groups.map(({ status, tasks: statusTasks }) => (
        <section
          key={status.id}
          aria-labelledby={`${status.id}-heading`}
          className="overflow-hidden rounded-[var(--radius)] border border-border bg-card shadow-sm"
        >
          <header className="flex items-center justify-between border-b border-border bg-muted/60 px-4 py-3">
            <div className="flex items-center gap-2">
              <span
                aria-hidden="true"
                className="size-2.5 rounded-full"
                style={{ backgroundColor: status.color }}
              />
              <h2 id={`${status.id}-heading`} className="text-sm font-semibold">
                {status.name}
              </h2>
            </div>
            <span className="rounded-full bg-card px-2 py-0.5 text-xs font-medium text-muted-foreground">
              {statusTasks.length}
            </span>
          </header>
          <div className="divide-y divide-border">
            {statusTasks.length === 0 ? (
              listIsEmpty ? null : (
                <p className="px-4 py-5 text-sm text-muted-foreground">No tasks here.</p>
              )
            ) : (
              statusTasks.map((task) => renderRow(task, 0))
            )}
          </div>
          <div className="border-t border-border p-3">
            <AddTaskButton
              label={`New task in ${status.name}`}
              ariaLabel={`Add task in ${status.name}`}
              pending={create.isPending}
              onSubmit={(title) => create.mutate({ listId, title, statusId: status.id })}
            />
          </div>
        </section>
      ))}
    </div>
  );
}
