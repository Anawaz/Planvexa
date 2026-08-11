"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useMemo, useState } from "react";
import { Avatar } from "@/components/ui/Avatar";
import { Button, buttonStyles } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { useAppContext } from "@/lib/app-context/AppContext";
import { useMemberDirectory } from "@/lib/members";
import {
  getMyWorkPreferences,
  listMyTasks,
  listStatusSchemes,
  listTasksCreatedByMe,
  listTasksWatching,
  saveMyWorkPreferences,
} from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";
import type { MyWorkPreferences, MyWorkSection, MyWorkSortBy, Priority, Task } from "@/lib/work/types";
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

const DEFAULT_PREFERENCES: MyWorkPreferences = { sortBy: "dueDate", hiddenSections: [] };

// Highest priority first when sorting by priority; irrelevant for the other two sort orders.
const PRIORITY_RANK: Record<Priority, number> = { Urgent: 4, High: 3, Normal: 2, Low: 1, None: 0 };

/** Personal sort choice (product spec section 15) applied client-side, since every task for the
 * relevant scope is already fetched in full — no server round-trip needed to reorder them. */
function sortByPreference(tasks: Task[], sortBy: MyWorkSortBy): Task[] {
  const sorted = [...tasks];
  if (sortBy === "priority") {
    sorted.sort((a, b) => PRIORITY_RANK[b.priority] - PRIORITY_RANK[a.priority]);
  } else if (sortBy === "title") {
    sorted.sort((a, b) => a.title.localeCompare(b.title));
  } else {
    sorted.sort((a, b) => (a.dueDate ?? "9999-99-99").localeCompare(b.dueDate ?? "9999-99-99"));
  }
  return sorted;
}

function dateOnly(date: Date) {
  const next = new Date(date);
  next.setHours(0, 0, 0, 0);
  return next;
}

function groupMyWork(tasks: Task[], sortBy: MyWorkSortBy): DueGroup[] {
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

  groups.forEach((group) => {
    group.tasks = sortByPreference(group.tasks, sortBy);
  });

  return groups;
}

export function MyWorkPageClient() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  // `?task=` is how a Favourites/Recent/search entry for a task lands here with the drawer already
  // open — read live (like ListPageClient) so clicking another such link while already on this page
  // (no remount) still opens the new task instead of leaving a stale drawer.
  const selectedTaskId = searchParams.get("task");

  function openTask(taskId: string | null) {
    const params = new URLSearchParams(searchParams.toString());

    if (taskId) {
      params.set("task", taskId);
      router.push(`${pathname}?${params.toString()}`, { scroll: false });
      return;
    }

    params.delete("task");
    router.replace(`${pathname}?${params.toString()}`, { scroll: false });
  }
  // `?new=1` is how the command palette's "New task" lands here with the dialog already open;
  // closing strips the param so a second visit from the palette is a fresh navigation.
  const [quickAddOpen, setQuickAddOpen] = useState(false);
  const wantsQuickAdd = searchParams.get("new") === "1";
  const showQuickAdd = quickAddOpen || wantsQuickAdd;
  const { getLabel, getInitials, getAvatarUrl } = useMemberDirectory();
  // My Work defaults to the currently active Workspace but, per product spec section 15 ("personal
  // cross-Workspace or Workspace-filtered view"), a member can point it at any other Workspace they
  // belong to without switching the whole app shell's active Workspace.
  const { workspaces, currentWorkspace } = useAppContext();
  const [workspaceFilter, setWorkspaceFilter] = useState<string | undefined>(undefined);
  const effectiveWorkspaceId = workspaceFilter ?? currentWorkspace?.id;

  function closeQuickAdd() {
    setQuickAddOpen(false);

    if (wantsQuickAdd) {
      router.replace(pathname, { scroll: false });
    }
  }

  const tasksQuery = useQuery({
    queryKey: workKeys.myTasks(effectiveWorkspaceId),
    queryFn: () => listMyTasks(effectiveWorkspaceId),
    enabled: Boolean(effectiveWorkspaceId),
  });
  const schemesQuery = useQuery({
    queryKey: workKeys.statusSchemes(),
    queryFn: listStatusSchemes,
  });
  const createdQuery = useQuery({
    queryKey: workKeys.createdByMeTasks(effectiveWorkspaceId),
    queryFn: () => listTasksCreatedByMe(effectiveWorkspaceId),
    enabled: Boolean(effectiveWorkspaceId),
  });
  const watchingQuery = useQuery({
    queryKey: workKeys.watchingTasks(effectiveWorkspaceId),
    queryFn: () => listTasksWatching(effectiveWorkspaceId),
    enabled: Boolean(effectiveWorkspaceId),
  });
  const queryClient = useQueryClient();
  const preferencesQuery = useQuery({
    queryKey: workKeys.myWorkPreferences(),
    queryFn: getMyWorkPreferences,
  });
  const preferences = preferencesQuery.data ?? DEFAULT_PREFERENCES;
  const savePreferencesMutation = useMutation({
    mutationFn: saveMyWorkPreferences,
    onSuccess: (saved) => queryClient.setQueryData(workKeys.myWorkPreferences(), saved),
  });

  function setSortBy(sortBy: MyWorkSortBy) {
    savePreferencesMutation.mutate({ ...preferences, sortBy });
  }

  function toggleSection(section: MyWorkSection, visible: boolean) {
    const hiddenSections = visible
      ? preferences.hiddenSections.filter((s) => s !== section)
      : [...preferences.hiddenSections, section];
    savePreferencesMutation.mutate({ ...preferences, hiddenSections });
  }

  const tasks = useMemo(() => tasksQuery.data ?? [], [tasksQuery.data]);
  const createdTasks = useMemo(
    () => sortByPreference(createdQuery.data ?? [], preferences.sortBy),
    [createdQuery.data, preferences.sortBy],
  );
  const watchingTasks = useMemo(
    () => sortByPreference(watchingQuery.data ?? [], preferences.sortBy),
    [watchingQuery.data, preferences.sortBy],
  );
  const groups = useMemo(() => groupMyWork(tasks, preferences.sortBy), [tasks, preferences.sortBy]);
  const showCreated = !preferences.hiddenSections.includes("created");
  const showWatching = !preferences.hiddenSections.includes("watching");
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
        <div className="flex flex-wrap items-center gap-3">
          {workspaces.length > 1 ? (
            <label className="flex items-center gap-2 text-sm font-medium">
              <span className="text-muted-foreground">Workspace</span>
              <select
                className="rounded-lg border border-border bg-card px-3 py-2 text-sm shadow-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                value={effectiveWorkspaceId ?? ""}
                aria-label="Filter My Work by workspace"
                onChange={(event) => setWorkspaceFilter(event.currentTarget.value)}
              >
                {workspaces.map((workspace) => (
                  <option key={workspace.id} value={workspace.id}>{workspace.name}</option>
                ))}
              </select>
            </label>
          ) : null}
          <label className="flex items-center gap-2 text-sm font-medium">
            <span className="text-muted-foreground">Sort by</span>
            <select
              className="rounded-lg border border-border bg-card px-3 py-2 text-sm shadow-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              value={preferences.sortBy}
              aria-label="Sort My Work"
              onChange={(event) => setSortBy(event.currentTarget.value as MyWorkSortBy)}
            >
              <option value="dueDate">Due date</option>
              <option value="priority">Priority</option>
              <option value="title">Title</option>
            </select>
          </label>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              className="size-4 accent-[var(--primary)]"
              checked={showCreated}
              onChange={(event) => toggleSection("created", event.currentTarget.checked)}
            />
            Created by me
          </label>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              className="size-4 accent-[var(--primary)]"
              checked={showWatching}
              onChange={(event) => toggleSection("watching", event.currentTarget.checked)}
            />
            Watching
          </label>
          <Button type="button" onClick={() => setQuickAddOpen(true)}>
            <span aria-hidden="true">+</span> New task
          </Button>
        </div>
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
                        onClick={() => openTask(task.id)}
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
                          <Avatar
                            key={userId}
                            title={getLabel(userId)}
                            avatarUrl={getAvatarUrl(userId)}
                            initials={getInitials(userId)}
                            className="grid size-7 place-items-center rounded-full border border-border bg-card text-[0.7rem] font-semibold text-muted-foreground"
                          />
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
      {showCreated ? (
      <section aria-labelledby="my-work-created-title" className="space-y-3">
        <h2 id="my-work-created-title" className="text-lg font-semibold tracking-tight">
          Created by me
        </h2>
        {createdQuery.isLoading ? (
          <div className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
            Loading tasks you created…
          </div>
        ) : createdQuery.isError ? (
          <div
            role="alert"
            className="rounded-[var(--radius)] border border-red-200 bg-red-50 p-6 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
          >
            Unable to load tasks you created.
          </div>
        ) : createdTasks.length === 0 ? (
          <p className="rounded-[var(--radius)] border border-dashed border-border p-4 text-sm text-muted-foreground">
            You haven&apos;t created any tasks yet.
          </p>
        ) : (
          <ul className="space-y-2">
            {createdTasks.map((task) => {
              const status = findStatus(statuses, task.statusId);

              return (
                <li
                  key={task.id}
                  className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-border bg-background p-3"
                >
                  <button
                    type="button"
                    className="text-left text-sm font-semibold hover:text-primary focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                    onClick={() => openTask(task.id)}
                  >
                    {task.title}
                  </button>
                  <div className="flex flex-wrap items-center gap-2 text-xs">
                    <span
                      className="rounded-full border px-2 py-0.5 font-medium"
                      style={statusBadgeStyle(status)}
                    >
                      {status?.name ?? "Unknown"}
                    </span>
                    <span className={dueDateClassName(task.dueDate, task.isCompleted)}>
                      {formatDate(task.dueDate)}
                    </span>
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </section>
      ) : null}
      {showWatching ? (
      <section aria-labelledby="my-work-watching-title" className="space-y-3">
        <h2 id="my-work-watching-title" className="text-lg font-semibold tracking-tight">
          Watching
        </h2>
        {watchingQuery.isLoading ? (
          <div className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
            Loading tasks you watch…
          </div>
        ) : watchingQuery.isError ? (
          <div
            role="alert"
            className="rounded-[var(--radius)] border border-red-200 bg-red-50 p-6 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
          >
            Unable to load tasks you watch.
          </div>
        ) : watchingTasks.length === 0 ? (
          <p className="rounded-[var(--radius)] border border-dashed border-border p-4 text-sm text-muted-foreground">
            You aren&apos;t watching any tasks yet.
          </p>
        ) : (
          <ul className="space-y-2">
            {watchingTasks.map((task) => {
              const status = findStatus(statuses, task.statusId);

              return (
                <li
                  key={task.id}
                  className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-border bg-background p-3"
                >
                  <button
                    type="button"
                    className="text-left text-sm font-semibold hover:text-primary focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                    onClick={() => openTask(task.id)}
                  >
                    {task.title}
                  </button>
                  <div className="flex flex-wrap items-center gap-2 text-xs">
                    <span
                      className="rounded-full border px-2 py-0.5 font-medium"
                      style={statusBadgeStyle(status)}
                    >
                      {status?.name ?? "Unknown"}
                    </span>
                    <span className={dueDateClassName(task.dueDate, task.isCompleted)}>
                      {formatDate(task.dueDate)}
                    </span>
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </section>
      ) : null}
      {showQuickAdd ? (
        <QuickAddTask onClose={closeQuickAdd} onCreated={openTask} />
      ) : null}
      <TaskDetailPanel
        taskId={selectedTaskId}
        open={Boolean(selectedTaskId)}
        statuses={statuses}
        onOpenTask={openTask}
        onClose={() => openTask(null)}
      />
    </section>
  );
}
