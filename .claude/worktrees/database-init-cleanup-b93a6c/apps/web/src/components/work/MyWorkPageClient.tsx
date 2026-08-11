"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useMemo, useState } from "react";
import { Button, buttonStyles } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { useMemberDirectory } from "@/lib/members";
import { listMyTasks, listStatusSchemes } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";
import type { Task } from "@/lib/work/types";
import {
  dueDateClassName,
  findStatus,
  formatDate,
  priorityClassName,
  statusBadgeStyle,
} from "./helpers";
import { QuickAddTask } from "./QuickAddTask";
import { TaskDetailPanel } from "./TaskDetailPanel";

type DueGroup = {
  id: "overdue" | "today" | "week" | "later";
  title: string;
  tasks: Task[];
};

function dateOnly(date: Date) {
  const next = new Date(date);
  next.setHours(0, 0, 0, 0);
  return next;
}

function groupMyWork(tasks: Task[]): DueGroup[] {
  const today = dateOnly(new Date());
  const weekEnd = new Date(today);
  weekEnd.setDate(weekEnd.getDate() + 7);

  const groups: DueGroup[] = [
    { id: "overdue", title: "Overdue", tasks: [] },
    { id: "today", title: "Today", tasks: [] },
    { id: "week", title: "This week", tasks: [] },
    { id: "later", title: "Later", tasks: [] },
  ];

  tasks.forEach((task) => {
    if (!task.dueDate) {
      groups[3].tasks.push(task);
      return;
    }

    const due = dateOnly(new Date(`${task.dueDate}T00:00:00`));

    if (due < today && !task.isCompleted) {
      groups[0].tasks.push(task);
    } else if (due.getTime() === today.getTime()) {
      groups[1].tasks.push(task);
    } else if (due <= weekEnd) {
      groups[2].tasks.push(task);
    } else {
      groups[3].tasks.push(task);
    }
  });

  return groups;
}

export function MyWorkPageClient() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  // `?task=` is how a Favourites/Recent/search entry for a task lands here with the drawer already open.
  const [selectedTaskId, setSelectedTaskId] = useState<string | null>(() => searchParams.get("task"));
  // `?new=1` is how the command palette's "New task" lands here with the dialog already open;
  // closing strips the param so a second visit from the palette is a fresh navigation.
  const [quickAddOpen, setQuickAddOpen] = useState(false);
  const wantsQuickAdd = searchParams.get("new") === "1";
  const showQuickAdd = quickAddOpen || wantsQuickAdd;
  const { getLabel, getInitials } = useMemberDirectory();

  function closeQuickAdd() {
    setQuickAddOpen(false);

    if (wantsQuickAdd) {
      router.replace(pathname, { scroll: false });
    }
  }

  const tasksQuery = useQuery({
    queryKey: workKeys.myTasks(),
    queryFn: listMyTasks,
  });
  const schemesQuery = useQuery({
    queryKey: workKeys.statusSchemes(),
    queryFn: listStatusSchemes,
  });
  const tasks = useMemo(() => tasksQuery.data ?? [], [tasksQuery.data]);
  const groups = useMemo(() => groupMyWork(tasks), [tasks]);
  // Four "Nothing assigned here." boxes say the same thing four times; one zero-state with the
  // next action replaces them when the whole page is empty.
  const nothingAssigned = tasks.length === 0;
  // My Work spans lists, so every scheme's statuses are in play.
  const statuses = useMemo(
    () => (schemesQuery.data ?? []).flatMap((scheme) => scheme.statuses),
    [schemesQuery.data],
  );

  return (
    <section aria-labelledby="my-work-title" className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Personal focus</p>
          <h1 id="my-work-title" className="mt-2 text-3xl font-semibold tracking-tight">
            My Work
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            Tasks assigned to you, grouped by due date.
          </p>
        </div>
        <Button type="button" onClick={() => setQuickAddOpen(true)}>
          <span aria-hidden="true">+</span> New task
        </Button>
      </div>
      {tasksQuery.isLoading ? (
        <div className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
          Loading assignments…
        </div>
      ) : tasksQuery.isError ? (
        <div
          role="alert"
          className="rounded-[var(--radius)] border border-red-200 bg-red-50 p-6 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
        >
          <p className="font-semibold">Unable to load assignments.</p>
          <p className="mt-2">
            Refresh the page or try again after the work API is available.
          </p>
        </div>
      ) : nothingAssigned ? (
        <EmptyState
          title="Nothing assigned to you yet"
          description="Tasks land here as soon as someone assigns them to you. Create one for yourself, or browse the workspace to find work to pick up."
        >
          <Button type="button" onClick={() => setQuickAddOpen(true)}>
            <span aria-hidden="true">+</span> New task
          </Button>
          <Link href="/app/spaces" className={buttonStyles({ variant: "secondary" })}>
            Browse spaces
          </Link>
        </EmptyState>
      ) : (
        <div className="grid gap-4 xl:grid-cols-4">
          {groups.map((group) => (
            <section
              key={group.id}
              aria-labelledby={`my-work-${group.id}`}
              className="rounded-[var(--radius)] border border-border bg-card shadow-sm"
            >
              <header className="flex items-center justify-between border-b border-border bg-muted/60 px-4 py-3">
                <h2 id={`my-work-${group.id}`} className="text-sm font-semibold">
                  {group.title}
                </h2>
                <span className="rounded-full bg-card px-2 py-0.5 text-xs font-medium text-muted-foreground">
                  {group.tasks.length}
                </span>
              </header>
              <div className="space-y-3 p-3">
                {group.tasks.length === 0 ? (
                  <p className="rounded-lg border border-dashed border-border p-4 text-sm text-muted-foreground">
                    Nothing assigned here.
                  </p>
                ) : (
                  group.tasks.map((task) => {
                    const status = findStatus(statuses, task.statusId);

                    return (
                    <article
                      key={task.id}
                      className="rounded-xl border border-border bg-background p-3"
                    >
                      <button
                        type="button"
                        className="text-left text-sm font-semibold hover:text-primary focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                        onClick={() => setSelectedTaskId(task.id)}
                      >
                        {task.title}
                      </button>
                      <div className="mt-2 flex flex-wrap items-center gap-2 text-xs">
                        <span
                          className="rounded-full border px-2 py-0.5 font-medium"
                          style={statusBadgeStyle(status)}
                        >
                          {status?.name ?? "Unknown"}
                        </span>
                        <span className={priorityClassName(task.priority)}>
                          {task.priority}
                        </span>
                        <span className={dueDateClassName(task.dueDate, task.isCompleted)}>
                          {formatDate(task.dueDate)}
                        </span>
                      </div>
                      <div className="mt-3 flex flex-wrap gap-1">
                        {task.assigneeUserIds.map((userId) => (
                          <span
                            key={userId}
                            title={getLabel(userId)}
                            className="grid size-7 place-items-center rounded-full border border-border bg-card text-[0.7rem] font-semibold text-muted-foreground"
                          >
                            {getInitials(userId)}
                          </span>
                        ))}
                      </div>
                    </article>
                    );
                  })
                )}
              </div>
            </section>
          ))}
        </div>
      )}
      {showQuickAdd ? (
        <QuickAddTask onClose={closeQuickAdd} onCreated={setSelectedTaskId} />
      ) : null}
      <TaskDetailPanel
        taskId={selectedTaskId}
        open={Boolean(selectedTaskId)}
        statuses={statuses}
        onOpenTask={setSelectedTaskId}
        onClose={() => setSelectedTaskId(null)}
      />
    </section>
  );
}
