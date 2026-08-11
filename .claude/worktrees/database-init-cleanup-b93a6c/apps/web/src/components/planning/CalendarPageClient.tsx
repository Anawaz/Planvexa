"use client";

import {
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import type { ReactNode } from "react";
import { Button } from "@/components/ui/Button";
import { getCalendar } from "@/lib/planning/client";
import { planningKeys } from "@/lib/planning/queries";
import type { CalendarTask } from "@/lib/planning/types";
import { listMyTasks, updateTask } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";
import { cn } from "@/lib/utils";
import {
  addDays,
  addMonths,
  dayKey,
  formatLongDate,
  formatShortDate,
  startOfUtcMonth,
  startOfUtcWeek,
} from "./helpers";

const weekdays = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
type CalendarMode = "month" | "week" | "day";
const modes: CalendarMode[] = ["month", "week", "day"];

const priorityClasses: Record<string, string> = {
  Urgent: "border-red-300 bg-red-100 text-red-700 dark:border-red-800 dark:bg-red-950 dark:text-red-300",
  High: "border-amber-300 bg-amber-100 text-amber-700 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-300",
  Normal:
    "border-blue-300 bg-blue-100 text-blue-700 dark:border-blue-800 dark:bg-blue-950 dark:text-blue-300",
  Low: "border-emerald-300 bg-emerald-100 text-emerald-700 dark:border-emerald-800 dark:bg-emerald-950 dark:text-emerald-300",
};

function buildCalendarDays(month: Date) {
  const monthStart = startOfUtcMonth(month);
  const gridStart = startOfUtcWeek(monthStart);
  return Array.from({ length: 42 }, (_, index) => addDays(gridStart, index));
}

function groupTasksByDay(tasks: CalendarTask[]) {
  return tasks.reduce<Map<string, CalendarTask[]>>((map, task) => {
    const key = dayKey(task.dueDate);
    map.set(key, [...(map.get(key) ?? []), task]);
    return map;
  }, new Map());
}

/**  . uc(a) task chip, draggable onto any day cell to reschedule its due date. */
function TaskChip({ task, disabled }: { task: CalendarTask; disabled?: boolean }) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: `task:${task.id}`,
    data: { taskId: task.id },
    disabled,
  });

  return (
    <button
      ref={setNodeRef}
      type="button"
      {...listeners}
      {...attributes}
      className={cn(
        "w-full rounded-lg border px-2 py-1.5 text-left text-xs font-medium shadow-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring",
        priorityClasses[task.priority] ?? priorityClasses.Normal,
        task.isCompleted && "line-through opacity-70",
        isDragging && "opacity-40",
        !disabled && "cursor-grab active:cursor-grabbing",
      )}
      style={transform ? { transform: `translate(${transform.x}px, ${transform.y}px)` } : undefined}
      aria-label={`${task.title}, ${task.priority} priority${task.isCompleted ? ", completed" : ""}. Draggable onto another day to reschedule.`}
    >
      {task.title}
    </button>
  );
}

/** A droppable day cell -- accepts a dragged TaskChip (or unscheduled-panel item) to set its due date. */
function DayCell({ day, children, className }: { day: Date; children: ReactNode; className?: string }) {
  const { setNodeRef, isOver } = useDroppable({ id: `day:${dayKey(day)}`, data: { date: dayKey(day) } });

  return (
    <section
      ref={setNodeRef}
      aria-label={formatLongDate(day)}
      className={cn(
        "min-h-36 bg-card p-3 outline-none transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-inset focus-visible:outline-ring",
        isOver && "bg-primary/10 ring-2 ring-inset ring-primary",
        className,
      )}
      role="gridcell"
      tabIndex={0}
    >
      {children}
    </section>
  );
}

/**  . uc(t)asks assigned to the caller with no due date -- drag one onto a day cell to schedule it. */
function UnscheduledPanel({ disabled }: { disabled: boolean }) {
  const myTasksQuery = useQuery({ queryKey: workKeys.myTasks(), queryFn: listMyTasks });
  const unscheduled = (myTasksQuery.data ?? []).filter((task) => !task.dueDate && !task.isCompleted);

  return (
    <section
      aria-labelledby="unscheduled-title"
      className="w-full shrink-0 rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm lg:w-64"
    >
      <h2 id="unscheduled-title" className="text-sm font-semibold">
        Unscheduled
      </h2>
      <p className="mt-1 text-xs text-muted-foreground">Drag a task onto a day to give it a due date.</p>
      <ul className="mt-3 space-y-2">
        {unscheduled.length === 0 ? (
          <li className="text-xs text-muted-foreground">Nothing unscheduled — nice.</li>
        ) : (
          unscheduled.map((task) => (
            <li key={task.id}>
              <TaskChip
                task={{
                  id: task.id,
                  title: task.title,
                  dueDate: "",
                  isCompleted: task.isCompleted,
                  priority: task.priority,
                  assigneeUserIds: task.assigneeUserIds,
                }}
                disabled={disabled}
              />
            </li>
          ))
        )}
      </ul>
    </section>
  );
}

export function CalendarPageClient() {
  const [mode, setMode] = useState<CalendarMode>("month");
  const [anchor, setAnchor] = useState(() => new Date());
  const queryClient = useQueryClient();

  const monthDays = useMemo(() => buildCalendarDays(anchor), [anchor]);
  const weekDays = useMemo(() => {
    const start = startOfUtcWeek(anchor);
    return Array.from({ length: 7 }, (_, index) => addDays(start, index));
  }, [anchor]);
  const visibleDays = useMemo(
    () => (mode === "month" ? monthDays : mode === "week" ? weekDays : [anchor]),
    [mode, monthDays, weekDays, anchor],
  );

  const params = useMemo(
    () => ({
      from: visibleDays[0].toISOString(),
      to: addDays(visibleDays[visibleDays.length - 1], 1).toISOString(),
      scopeId: "workspace-planvexa-demo",
    }),
    [visibleDays],
  );
  const calendarQuery = useQuery({
    queryKey: planningKeys.calendar(params),
    queryFn: () => getCalendar(params),
  });
  const tasksByDay = useMemo(() => groupTasksByDay(calendarQuery.data ?? []), [calendarQuery.data]);

  const reschedule = useMutation({
    mutationFn: ({ taskId, dueDate }: { taskId: string; dueDate: string }) =>
      updateTask(taskId, { dueDate }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: planningKeys.calendarRoot() });
      void queryClient.invalidateQueries({ queryKey: workKeys.myTasks() });
      void queryClient.invalidateQueries({ queryKey: workKeys.all });
    },
  });

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 8 } }),
    useSensor(KeyboardSensor),
  );

  function handleDragEnd(event: DragEndEvent) {
    const taskId = event.active.data.current?.taskId as string | undefined;
    const date = event.over?.data.current?.date as string | undefined;
    if (taskId && date) {
      reschedule.mutate({ taskId, dueDate: date });
    }
  }

  const currentMonth = anchor.getUTCMonth();
  const title =
    mode === "day"
      ? formatLongDate(anchor)
      : new Intl.DateTimeFormat("en", { month: "long", year: "numeric" }).format(anchor);

  function step(direction: 1 | -1) {
    if (mode === "month") setAnchor((current) => addMonths(current, direction));
    else if (mode === "week") setAnchor((current) => addDays(current, direction * 7));
    else setAnchor((current) => addDays(current, direction));
  }

  return (
    <DndContext sensors={sensors} onDragEnd={handleDragEnd}>
      <section aria-labelledby="calendar-title" className="space-y-6">
        <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
          <div>
            <p className="text-sm font-medium text-primary">Planning</p>
            <h1 id="calendar-title" className="mt-2 text-3xl font-semibold tracking-tight">
              Calendar
            </h1>
            <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
              Month, week or day planning grid. Drag a task onto a day to reschedule it.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <fieldset className="inline-flex rounded-xl border border-border bg-card p-1 shadow-sm" aria-label="Calendar view mode">
              <legend className="sr-only">Calendar view mode</legend>
              {modes.map((option) => (
                <Button
                  key={option}
                  type="button"
                  size="sm"
                  variant={mode === option ? "primary" : "ghost"}
                  aria-pressed={mode === option}
                  className="capitalize"
                  onClick={() => setMode(option)}
                >
                  {option}
                </Button>
              ))}
            </fieldset>
            <Button type="button" variant="outline" size="sm" aria-label="Previous" onClick={() => step(-1)}>
              Previous
            </Button>
            <Button type="button" variant="secondary" size="sm" onClick={() => setAnchor(new Date())}>
              Today
            </Button>
            <Button type="button" variant="outline" size="sm" aria-label="Next" onClick={() => step(1)}>
              Next
            </Button>
          </div>
        </div>

        <div className="flex flex-col gap-4 lg:flex-row">
          <section
            aria-labelledby="calendar-range"
            className="flex-1 rounded-[var(--radius)] border border-border bg-card shadow-sm"
          >
            <header className="flex flex-col gap-2 border-b border-border p-4 sm:flex-row sm:items-center sm:justify-between">
              <div>
                <h2 id="calendar-range" className="text-lg font-semibold">
                  {title}
                </h2>
                <p className="text-xs text-muted-foreground">
                  Showing {formatShortDate(visibleDays[0])} – {formatShortDate(visibleDays[visibleDays.length - 1])}
                </p>
              </div>
              <span className="rounded-full bg-primary/10 px-3 py-1 text-xs font-semibold text-primary">
                {calendarQuery.data?.length ?? 0} due items
              </span>
            </header>

            {!calendarQuery.isLoading && (calendarQuery.data?.length ?? 0) === 0 ? (
              <p role="status" className="border-b border-border px-4 py-3 text-sm text-muted-foreground">
                Nothing is due in this range. Give a task a due date in any list, or drag one in from Unscheduled.
              </p>
            ) : null}

            {mode === "month" ? (
              <div className="hidden grid-cols-7 border-b border-border bg-muted/70 sm:grid" role="row">
                {weekdays.map((day) => (
                  <div key={day} role="columnheader" className="px-3 py-2 text-center text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                    {day}
                  </div>
                ))}
              </div>
            ) : null}

            <div
              className={cn(
                "grid grid-cols-1 gap-px bg-border",
                mode === "month" && "sm:grid-cols-7",
                mode === "week" && "sm:grid-cols-7",
              )}
              role="grid"
            >
              {visibleDays.map((day) => {
                const key = dayKey(day);
                const dayTasks = tasksByDay.get(key) ?? [];
                const isCurrentMonth = mode !== "month" || day.getUTCMonth() === currentMonth;
                const isToday = key === dayKey(new Date());

                return (
                  <DayCell key={key} day={day} className={cn(!isCurrentMonth && "bg-background text-muted-foreground")}>
                    <div className="flex items-center justify-between gap-2">
                      <span
                        className={cn(
                          "grid size-7 place-items-center rounded-full text-sm font-semibold",
                          isToday && "bg-primary text-primary-foreground",
                        )}
                      >
                        {mode === "day" ? formatShortDate(day) : day.getUTCDate()}
                      </span>
                      {dayTasks.length > 0 ? (
                        <span className="text-xs font-medium text-muted-foreground">{dayTasks.length}</span>
                      ) : null}
                    </div>
                    <div className="mt-3 space-y-2">
                      {dayTasks.map((task) => (
                        <TaskChip key={task.id} task={task} disabled={reschedule.isPending} />
                      ))}
                    </div>
                  </DayCell>
                );
              })}
            </div>
          </section>

          <UnscheduledPanel disabled={reschedule.isPending} />
        </div>
      </section>
    </DndContext>
  );
}
