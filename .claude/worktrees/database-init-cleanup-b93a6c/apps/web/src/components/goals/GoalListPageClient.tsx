"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import type { FormEvent } from "react";
import { Button, buttonStyles } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { createGoal, listGoals } from "@/lib/goals/client";
import { goalKeys } from "@/lib/goals/queries";
import type { GoalTargetType } from "@/lib/goals/types";

function statusBadgeClass(status: string) {
  switch (status) {
    case "OnTrack":
    case "Completed":
      return "bg-emerald-500/10 text-emerald-700 dark:text-emerald-400";
    case "AtRisk":
      return "bg-amber-500/10 text-amber-700 dark:text-amber-400";
    case "OffTrack":
      return "bg-red-500/10 text-red-700 dark:text-red-400";
    default:
      return "bg-muted text-muted-foreground";
  }
}

export function GoalListPageClient() {
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [targetType, setTargetType] = useState<GoalTargetType>("Numeric");
  const [targetValue, setTargetValue] = useState("100");

  const goalsQuery = useQuery({ queryKey: goalKeys.list(), queryFn: () => listGoals() });
  const createGoalMutation = useMutation({
    mutationFn: createGoal,
    onSuccess: () => {
      setName("");
      void queryClient.invalidateQueries({ queryKey: goalKeys.list() });
    },
  });

  function submitGoal(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!name.trim()) return;

    const now = new Date();
    const endDate = new Date(now);
    endDate.setDate(endDate.getDate() + 90);

    createGoalMutation.mutate({
      name: name.trim(),
      targetType,
      targetValue: targetType === "Numeric" ? Number(targetValue) || 100 : undefined,
      startDate: now.toISOString(),
      endDate: endDate.toISOString(),
    });
  }

  return (
    <section aria-labelledby="goals-title" className="space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">Goals</p>
        <h1 id="goals-title" className="mt-2 text-3xl font-semibold tracking-tight">
          Goals &amp; OKRs
        </h1>
        <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
          Track numeric-target OKRs or task-completion goals for the workspace.
        </p>
      </div>

      {createGoalMutation.error ? (
        <p
          role="alert"
          className="rounded-[var(--radius)] border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
        >
          This goal could not be saved: {(createGoalMutation.error as Error).message}
        </p>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[1fr_22rem]">
        <section className="grid gap-4 md:grid-cols-2" aria-label="Goal list">
          {goalsQuery.isLoading ? (
            <p className="rounded-[var(--radius)] border border-border bg-card p-4 text-sm text-muted-foreground">
              Loading goals…
            </p>
          ) : (goalsQuery.data ?? []).length === 0 ? (
            <EmptyState
              className="md:col-span-2"
              title="No goals yet"
              description="Create a numeric-target OKR or a task-completion goal with the form beside this list."
            />
          ) : (
            goalsQuery.data?.map((goal) => (
              <article
                key={goal.id}
                className="rounded-[var(--radius)] border border-border bg-card p-5 shadow-sm"
              >
                <div className="flex items-start justify-between gap-3">
                  <h2 className="truncate text-lg font-semibold">{goal.name}</h2>
                  <span className={`rounded-full px-2.5 py-1 text-xs font-semibold ${statusBadgeClass(goal.status)}`}>
                    {goal.status}
                  </span>
                </div>
                <p className="mt-2 text-sm text-muted-foreground">
                  {goal.targetType === "Numeric"
                    ? `${goal.currentValue ?? 0} / ${goal.targetValue ?? 0}`
                    : `${goal.completedLinkedTaskCount} / ${goal.linkedTaskCount} tasks`}
                </p>
                <div className="mt-3 h-2 rounded-full bg-muted">
                  <div
                    className="h-2 rounded-full bg-primary"
                    style={{ width: `${Math.min(100, Math.max(2, goal.percentComplete))}%` }}
                  />
                </div>
                <p className="mt-1 text-xs text-muted-foreground">{goal.percentComplete}% complete</p>
                <div className="mt-4">
                  <Link
                    href={`/app/goals/${goal.id}`}
                    className={buttonStyles({ variant: "primary", size: "sm" })}
                  >
                    Open goal
                  </Link>
                </div>
              </article>
            ))
          )}
        </section>

        <form
          onSubmit={submitGoal}
          className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm"
        >
          <h2 className="text-sm font-semibold">Create goal</h2>
          <p className="mt-1 text-xs text-muted-foreground">
            Numeric goals track a current/target value; task-completion goals track linked tasks.
          </p>
          <div className="mt-4 grid gap-3">
            <label className="grid gap-1 text-xs font-medium">
              Goal name
              <input
                value={name}
                onChange={(event) => setName(event.target.value)}
                className="h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              />
            </label>
            <label className="grid gap-1 text-xs font-medium">
              Style
              <select
                value={targetType}
                onChange={(event) => setTargetType(event.target.value as GoalTargetType)}
                className="h-10 rounded-lg border border-border bg-background px-3 text-sm"
              >
                <option value="Numeric">Numeric target</option>
                <option value="LinkedTasksRatio">Task completion</option>
              </select>
            </label>
            {targetType === "Numeric" ? (
              <label className="grid gap-1 text-xs font-medium">
                Target value
                <input
                  type="number"
                  min={1}
                  value={targetValue}
                  onChange={(event) => setTargetValue(event.target.value)}
                  className="h-10 rounded-lg border border-border bg-background px-3 text-sm"
                />
              </label>
            ) : null}
            <Button type="submit" size="sm" disabled={createGoalMutation.isPending}>
              Create goal
            </Button>
          </div>
        </form>
      </div>
    </section>
  );
}
