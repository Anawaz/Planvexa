"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import type { FormEvent } from "react";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { TaskSelect } from "@/components/work/TaskSelect";
import {
  addSprintItem,
  createSprint,
  getSprintBoard,
  listSprints,
  removeSprintItem,
} from "@/lib/planning/client";
import { planningKeys } from "@/lib/planning/queries";
import { cn } from "@/lib/utils";
import { dateInputToUtc, formatShortDate, toIsoDateInput } from "./helpers";

function defaultDateInput(dayOffset: number) {
  const date = new Date();
  date.setUTCDate(date.getUTCDate() + dayOffset);
  return toIsoDateInput(date);
}

export function SprintsPageClient() {
  const queryClient = useQueryClient();
  const [selectedSprintId, setSelectedSprintId] = useState<string | null>(null);
  const [sprintName, setSprintName] = useState("");
  const [startDate, setStartDate] = useState(defaultDateInput(0));
  const [endDate, setEndDate] = useState(defaultDateInput(13));
  const [taskId, setTaskId] = useState("");
  const [points, setPoints] = useState("3");
  const sprintsQuery = useQuery({
    queryKey: planningKeys.sprints(),
    queryFn: listSprints,
  });
  const sprints = sprintsQuery.data ?? [];
  const activeSprintId = selectedSprintId ?? sprints[0]?.id ?? "";
  const boardQuery = useQuery({
    queryKey: planningKeys.sprintBoard(activeSprintId),
    queryFn: () => getSprintBoard(activeSprintId),
    enabled: Boolean(activeSprintId),
  });
  const activeSprint = sprints.find((sprint) => sprint.id === activeSprintId);
  const createSprintMutation = useMutation({
    mutationFn: createSprint,
    onSuccess: (sprint) => {
      setSprintName("");
      setSelectedSprintId(sprint.id);
      void queryClient.invalidateQueries({ queryKey: planningKeys.sprints() });
    },
  });
  const addItemMutation = useMutation({
    mutationFn: ({ sprintId, input }: { sprintId: string; input: { taskId: string; points?: number } }) =>
      addSprintItem(sprintId, input),
    onSuccess: () => {
      setTaskId("");
      void queryClient.invalidateQueries({ queryKey: planningKeys.sprintBoard(activeSprintId) });
    },
  });
  const removeItemMutation = useMutation({
    mutationFn: ({ sprintId, taskId: itemTaskId }: { sprintId: string; taskId: string }) =>
      removeSprintItem(sprintId, itemTaskId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: planningKeys.sprintBoard(activeSprintId) });
    },
  });
  const mutationError =
    createSprintMutation.error ?? addItemMutation.error ?? removeItemMutation.error;

  function submitSprint(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!sprintName.trim()) {
      return;
    }

    createSprintMutation.mutate({
      name: sprintName.trim(),
      startUtc: dateInputToUtc(startDate),
      endUtc: dateInputToUtc(endDate),
    });
  }

  function submitItem(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!taskId.trim() || !activeSprintId) {
      return;
    }

    addItemMutation.mutate({
      sprintId: activeSprintId,
      input: {
        taskId: taskId.trim(),
        points: points ? Number(points) : undefined,
      },
    });
  }

  return (
    <section aria-labelledby="sprints-title" className="space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">Agile planning</p>
        <h1 id="sprints-title" className="mt-2 text-3xl font-semibold tracking-tight">
          Sprints
        </h1>
        <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
          Plan time-boxed work, review status columns, and track point totals across the sprint
          board.
        </p>
      </div>

      {mutationError ? (
        <p
          role="alert"
          className="rounded-[var(--radius)] border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
        >
          This sprint change could not be saved: {(mutationError as Error).message}
        </p>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[22rem_1fr]">
        <aside className="space-y-4">
          <section
            className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm"
            aria-labelledby="sprint-list-title"
          >
            <h2 id="sprint-list-title" className="text-sm font-semibold">
              Sprint list
            </h2>
            <div className="mt-3 space-y-2">
              {sprintsQuery.isLoading ? (
                <p className="text-sm text-muted-foreground">Loading sprints…</p>
              ) : sprints.length === 0 ? (
                <EmptyState
                  title="No sprints yet"
                  description="Use the sprint form on this page to plan your first iteration; its board appears here once it exists."
                />
              ) : (
                sprints.map((sprint) => (
                  <button
                    key={sprint.id}
                    type="button"
                    className={cn(
                      "w-full rounded-xl border p-3 text-left transition focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring motion-reduce:transition-none",
                      activeSprintId === sprint.id
                        ? "border-primary bg-primary/10"
                        : "border-border bg-background hover:bg-muted",
                    )}
                    aria-pressed={activeSprintId === sprint.id}
                    onClick={() => setSelectedSprintId(sprint.id)}
                  >
                    <span className="block text-sm font-semibold">{sprint.name}</span>
                    <span className="mt-1 block text-xs text-muted-foreground">
                      {formatShortDate(sprint.startUtc)} – {formatShortDate(sprint.endUtc)}
                    </span>
                    <span className="mt-2 inline-flex rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
                      {sprint.status}
                    </span>
                  </button>
                ))
              )}
            </div>
          </section>

          <form
            onSubmit={submitSprint}
            className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm"
          >
            <h2 className="text-sm font-semibold">Create sprint</h2>
            <div className="mt-3 grid gap-3">
              <label className="grid gap-1 text-xs font-medium">
                Name
                <input
                  value={sprintName}
                  onChange={(event) => setSprintName(event.target.value)}
                  className="h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                />
              </label>
              <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-1">
                <label className="grid gap-1 text-xs font-medium">
                  Start
                  <input
                    type="date"
                    value={startDate}
                    onChange={(event) => setStartDate(event.target.value)}
                    className="h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                  />
                </label>
                <label className="grid gap-1 text-xs font-medium">
                  End
                  <input
                    type="date"
                    value={endDate}
                    onChange={(event) => setEndDate(event.target.value)}
                    className="h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                  />
                </label>
              </div>
              <Button type="submit" size="sm" disabled={createSprintMutation.isPending}>
                Add sprint
              </Button>
            </div>
          </form>
        </aside>

        <section
          className="rounded-[var(--radius)] border border-border bg-card shadow-sm"
          aria-labelledby="sprint-board-title"
        >
          <header className="flex flex-col gap-3 border-b border-border p-4 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <h2 id="sprint-board-title" className="text-lg font-semibold">
                {activeSprint?.name ?? "Select a sprint"}
              </h2>
              <p className="mt-1 text-xs text-muted-foreground">
                {activeSprint
                  ? `${formatShortDate(activeSprint.startUtc)} – ${formatShortDate(activeSprint.endUtc)}`
                  : "Sprint board columns render when a sprint is selected."}
              </p>
            </div>
            <form onSubmit={submitItem} className="flex flex-wrap items-end gap-2">
              <label className="grid gap-1 text-xs font-medium">
                Task
                <TaskSelect value={taskId} onChange={setTaskId} aria-label="Task" className="h-9" />
              </label>
              <label className="grid gap-1 text-xs font-medium">
                Points
                <input
                  type="number"
                  min={0}
                  value={points}
                  onChange={(event) => setPoints(event.target.value)}
                  className="h-9 w-24 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                />
              </label>
              <Button type="submit" size="sm" disabled={!activeSprintId || addItemMutation.isPending}>
                Add item
              </Button>
            </form>
          </header>

          {boardQuery.isLoading ? (
            <p className="p-4 text-sm text-muted-foreground">Loading sprint board…</p>
          ) : (
            <div className="grid gap-4 p-4 xl:grid-cols-4">
              {(boardQuery.data ?? []).map((column) => {
                const totalPoints = column.tasks.reduce((total, task) => total + (task.points ?? 0), 0);

                return (
                  <section
                    key={column.statusId}
                    className="rounded-xl border border-border bg-background"
                    aria-labelledby={`sprint-column-${column.statusId}`}
                  >
                    <header className="flex items-center justify-between border-b border-border px-3 py-2">
                      <h3 id={`sprint-column-${column.statusId}`} className="text-sm font-semibold">
                        {column.statusName}
                      </h3>
                      <span className="rounded-full bg-card px-2 py-0.5 text-xs font-semibold text-muted-foreground">
                        {totalPoints} pts
                      </span>
                    </header>
                    <div className="space-y-2 p-3">
                      {column.tasks.length === 0 ? (
                        <p className="rounded-lg border border-dashed border-border p-3 text-xs text-muted-foreground">
                          No tasks in this column.
                        </p>
                      ) : (
                        column.tasks.map((task) => (
                          <article
                            key={task.id}
                            className="rounded-lg border border-border bg-card p-3 shadow-sm"
                          >
                            <div className="flex items-start justify-between gap-2">
                              <h4 className="text-sm font-semibold">{task.title}</h4>
                              <Button
                                type="button"
                                size="sm"
                                variant="ghost"
                                className="h-7 shrink-0 px-2 text-xs text-red-600 hover:text-red-700 dark:text-red-400"
                                aria-label={`Remove ${task.title} from sprint`}
                                disabled={removeItemMutation.isPending}
                                onClick={() =>
                                  removeItemMutation.mutate({
                                    sprintId: activeSprintId,
                                    taskId: task.id,
                                  })
                                }
                              >
                                Remove
                              </Button>
                            </div>
                            <p className="mt-2 text-xs text-muted-foreground">
                              {task.points ?? 0} points · {task.id}
                            </p>
                          </article>
                        ))
                      )}
                    </div>
                  </section>
                );
              })}
            </div>
          )}
        </section>
      </div>
    </section>
  );
}
