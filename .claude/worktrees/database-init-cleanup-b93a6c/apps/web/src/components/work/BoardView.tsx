"use client";

import {
  closestCorners,
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import {
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import type { CSSProperties, ReactNode } from "react";
import { useMemo } from "react";
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
import { dropPosition, midpoint } from "./positioning";

type BoardViewProps = {
  tasks: Task[];
  statuses: StatusDefinition[];
  listId: string;
  /** The list has no tasks at all — the page owns the zero-state, so skip the per-column one. */
  listIsEmpty?: boolean;
  onOpenTask: (taskId: string) => void;
};

type BoardColumnProps = {
  status: StatusDefinition;
  tasks: Task[];
  children: ReactNode;
};

function BoardColumn({ status, tasks, children }: BoardColumnProps) {
  const { setNodeRef, isOver } = useDroppable({
    id: status.id,
    data: { type: "status", statusId: status.id },
  });

  return (
    <section
      ref={setNodeRef}
      aria-labelledby={`${status.id}-board-heading`}
      className={cn(
        "flex min-h-[24rem] min-w-[18rem] flex-1 flex-col rounded-[var(--radius)] border border-border bg-muted/40 transition-colors",
        isOver && "border-primary bg-primary/5",
      )}
    >
      <header className="flex items-center justify-between border-b border-border px-3 py-3">
        <div className="flex items-center gap-2">
          <span
            aria-hidden="true"
            className="size-2.5 rounded-full"
            style={{ backgroundColor: status.color }}
          />
          <h2 id={`${status.id}-board-heading`} className="text-sm font-semibold">
            {status.name}
          </h2>
        </div>
        <span className="rounded-full bg-card px-2 py-0.5 text-xs font-medium text-muted-foreground">
          {tasks.length}
        </span>
      </header>
      <SortableContext
        items={tasks.map((task) => task.id)}
        strategy={verticalListSortingStrategy}
      >
        <div className="flex flex-1 flex-col gap-3 p-3">{children}</div>
      </SortableContext>
    </section>
  );
}

type BoardCardProps = {
  task: Task;
  statuses: StatusDefinition[];
  subtaskCount: number;
  onOpenTask: (taskId: string) => void;
  onStatusChange: (taskId: string, statusId: string) => void;
};

function BoardCard({
  task,
  statuses,
  subtaskCount,
  onOpenTask,
  onStatusChange,
}: BoardCardProps) {
  const { getLabel, getInitials } = useMemberDirectory();
  const {
    attributes,
    isDragging,
    listeners,
    setActivatorNodeRef,
    setNodeRef,
    transform,
    transition,
  } = useSortable({
    id: task.id,
    data: { type: "task", taskId: task.id, statusId: task.statusId },
  });
  const status = findStatus(statuses, task.statusId);
  const style: CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
  };

  return (
    <article
      ref={setNodeRef}
      style={style}
      className={cn(
        "rounded-xl border border-border bg-card p-3 shadow-sm focus-within:ring-2 focus-within:ring-ring",
        isDragging && "opacity-60 shadow-lg",
      )}
    >
      <div className="flex items-start gap-2">
        <button
          ref={setActivatorNodeRef}
          type="button"
          className="mt-0.5 rounded px-1 text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          aria-label={`Drag ${task.title}`}
          {...attributes}
          {...listeners}
        >
          ⋮⋮
        </button>
        <div className="min-w-0 flex-1">
          <button
            type="button"
            className="text-left text-sm font-semibold leading-5 hover:text-primary focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            onClick={() => onOpenTask(task.id)}
          >
            {task.title}
          </button>
          <p className="mt-1 text-xs text-muted-foreground">{task.sequence}</p>
        </div>
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-2">
        <span
          className="rounded-full border px-2 py-0.5 text-xs font-medium"
          style={statusBadgeStyle(status)}
        >
          {status?.name ?? "Unknown"}
        </span>
        <span className={priorityClassName(task.priority)}>{task.priority}</span>
        <span className={cn("text-xs", dueDateClassName(task.dueDate, task.isCompleted))}>
          {formatDate(task.dueDate)}
        </span>
        {subtaskCount > 0 ? (
          <span className="rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
            {subtaskCount} subtask{subtaskCount === 1 ? "" : "s"}
          </span>
        ) : null}
      </div>
      <div className="mt-3 flex items-center justify-between gap-3">
        <div className="flex -space-x-1">
          {task.assigneeUserIds.map((userId) => (
            <span
              key={userId}
              title={getLabel(userId)}
              className="grid size-7 place-items-center rounded-full border border-card bg-muted text-[0.65rem] font-semibold"
            >
              {getInitials(userId)}
            </span>
          ))}
        </div>
        <label className="grid gap-1 text-xs text-muted-foreground">
          <span className="sr-only">Change status for {task.title}</span>
          <select
            value={task.statusId}
            className="rounded-lg border border-border bg-background px-2 py-1 text-xs text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            onChange={(event) => onStatusChange(task.id, event.target.value)}
          >
            {statuses.map((status) => (
              <option key={status.id} value={status.id}>
                {status.name}
              </option>
            ))}
          </select>
        </label>
      </div>
    </article>
  );
}

export function BoardView({
  tasks,
  statuses,
  listId,
  listIsEmpty = false,
  onOpenTask,
}: BoardViewProps) {
  const { moveTask } = useTaskMutations(statuses);
  const create = useWorkMutation(createTask);
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 8 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );
  // Subtasks stay off the board and surface as a count on their parent card, so a
  // column reads as real work items instead of a flat mix of parents and their children.
  const { childrenOf, roots } = useMemo(() => buildTaskTree(tasks), [tasks]);
  const statusGroups = useMemo(
    () => groupTasksByStatus(roots, statuses),
    [roots, statuses],
  );
  const statusIds = new Set(statuses.map((status) => status.id));

  function moveTaskToStatus(taskId: string, statusId: string) {
    const columnTasks = roots.filter(
      (task) => task.statusId === statusId && task.id !== taskId,
    );
    const nextPosition = midpoint(
      columnTasks.length === 0
        ? undefined
        : Math.max(...columnTasks.map((task) => task.position)),
    );

    moveTask.mutate({ taskId, input: { statusId, position: nextPosition } });
  }

  function handleDragEnd(event: DragEndEvent) {
    const activeTask = roots.find((task) => task.id === String(event.active.id));
    const overId = event.over ? String(event.over.id) : undefined;

    if (!activeTask || !overId) {
      return;
    }

    const overTask = roots.find((task) => task.id === overId);
    const targetStatusId = overTask?.statusId ?? (statusIds.has(overId) ? overId : undefined);

    if (!targetStatusId || overId === activeTask.id) {
      return;
    }

    const column = roots
      .filter((task) => task.statusId === targetStatusId)
      .sort((left, right) => left.position - right.position);
    const position = dropPosition(column, activeTask.id, overTask?.id);

    moveTask.mutate({ taskId: activeTask.id, input: { statusId: targetStatusId, position } });
  }

  return (
    <DndContext
      collisionDetection={closestCorners}
      sensors={sensors}
      onDragEnd={handleDragEnd}
    >
      {create.isError ? (
        <p
          role="alert"
          className="mb-4 rounded-[var(--radius)] border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
        >
          This task could not be created: {(create.error as Error).message}
        </p>
      ) : null}
      <div
        // `relative` is load-bearing: the per-card `sr-only` status labels are absolutely
        // positioned, and without a containing block inside this scroller they resolved against the
        // page and dragged the whole document's horizontal scroll out to the last column.
        className="relative flex gap-4 overflow-x-auto pb-2"
        aria-label="Kanban board with draggable task cards"
      >
        {statusGroups.map(({ status, tasks: statusTasks }) => (
          <BoardColumn key={status.id} status={status} tasks={statusTasks}>
            {statusTasks.length === 0 ? (
              // "Drop tasks here" only makes sense once there is something to drop.
              listIsEmpty ? null : (
                <p className="rounded-lg border border-dashed border-border bg-card/60 p-4 text-sm text-muted-foreground">
                  Drop tasks here.
                </p>
              )
            ) : (
              statusTasks.map((task) => (
                <BoardCard
                  key={task.id}
                  task={task}
                  statuses={statuses}
                  subtaskCount={childrenOf.get(task.id)?.length ?? 0}
                  onOpenTask={onOpenTask}
                  onStatusChange={moveTaskToStatus}
                />
              ))
            )}
            <AddTaskButton
              label={`New task in ${status.name}`}
              ariaLabel={`Add task in ${status.name}`}
              pending={create.isPending}
              onSubmit={(title) => create.mutate({ listId, title, statusId: status.id })}
            />
          </BoardColumn>
        ))}
      </div>
    </DndContext>
  );
}
