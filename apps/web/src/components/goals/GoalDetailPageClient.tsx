"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import type { FormEvent } from "react";
import { Button, buttonStyles } from "@/components/ui/Button";
import { QueryState } from "@/components/ui/QueryState";
import { ResourcePicker } from "@/components/ui/ResourcePicker";
import {
  addGoalComment,
  deleteGoal,
  getGoal,
  linkGoalKeyResult,
  linkGoalTask,
  listGoalComments,
  removeGoalKeyResult,
  unlinkGoalTask,
  updateGoal,
  updateGoalKeyResult,
} from "@/lib/goals/client";
import { goalKeys } from "@/lib/goals/queries";
import { formatGoalValue, type GoalKeyResult, type GoalUnit } from "@/lib/goals/types";

export function GoalDetailPageClient({ goalId }: { goalId: string }) {
  const queryClient = useQueryClient();
  const [taskIdDraft, setTaskIdDraft] = useState("");
  const [commentDraft, setCommentDraft] = useState("");
  const [currentValueDraft, setCurrentValueDraft] = useState("");
  const [krTitle, setKrTitle] = useState("");
  const [krTarget, setKrTarget] = useState("100");
  const [krCurrent, setKrCurrent] = useState("0");
  const [krUnit, setKrUnit] = useState<GoalUnit>("Number");
  const [editingKrId, setEditingKrId] = useState<string | null>(null);
  const [editDraft, setEditDraft] = useState({ title: "", current: "", target: "", unit: "Number" as GoalUnit });

  const goalQuery = useQuery({ queryKey: goalKeys.detail(goalId), queryFn: () => getGoal(goalId) });
  const commentsQuery = useQuery({
    queryKey: goalKeys.comments(goalId),
    queryFn: () => listGoalComments(goalId),
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: goalKeys.detail(goalId) });
    void queryClient.invalidateQueries({ queryKey: goalKeys.list() });
  };

  const linkMutation = useMutation({
    mutationFn: (taskId: string) => linkGoalTask(goalId, taskId),
    onSuccess: () => {
      setTaskIdDraft("");
      invalidate();
    },
  });
  const unlinkMutation = useMutation({
    mutationFn: (taskId: string) => unlinkGoalTask(goalId, taskId),
    onSuccess: invalidate,
  });
  const updateValueMutation = useMutation({
    mutationFn: (currentValue: number) => updateGoal(goalId, { currentValue }),
    onSuccess: () => {
      setCurrentValueDraft("");
      invalidate();
    },
  });
  const commentMutation = useMutation({
    mutationFn: (body: string) => addGoalComment(goalId, body),
    onSuccess: () => {
      setCommentDraft("");
      void queryClient.invalidateQueries({ queryKey: goalKeys.comments(goalId) });
    },
  });
  const deleteMutation = useMutation({ mutationFn: () => deleteGoal(goalId) });

  const addKeyResultMutation = useMutation({
    mutationFn: () =>
      linkGoalKeyResult(goalId, {
        title: krTitle.trim(),
        targetValue: Number(krTarget) || 1,
        currentValue: Number(krCurrent) || 0,
        unit: krUnit,
      }),
    onSuccess: () => {
      setKrTitle("");
      setKrTarget("100");
      setKrCurrent("0");
      setKrUnit("Number");
      invalidate();
    },
  });
  const updateKeyResultMutation = useMutation({
    mutationFn: (keyResultId: string) =>
      updateGoalKeyResult(goalId, keyResultId, {
        title: editDraft.title.trim() || undefined,
        currentValue: editDraft.current === "" ? undefined : Number(editDraft.current),
        targetValue: editDraft.target === "" ? undefined : Number(editDraft.target),
        unit: editDraft.unit,
      }),
    onSuccess: () => {
      setEditingKrId(null);
      invalidate();
    },
  });
  const removeKeyResultMutation = useMutation({
    mutationFn: (keyResultId: string) => removeGoalKeyResult(goalId, keyResultId),
    onSuccess: invalidate,
  });

  function startEditingKeyResult(keyResult: GoalKeyResult) {
    setEditingKrId(keyResult.id);
    setEditDraft({
      title: keyResult.title,
      current: String(keyResult.currentValue),
      target: String(keyResult.targetValue),
      unit: keyResult.unit,
    });
  }

  const goal = goalQuery.data?.goal;

  return (
    <section aria-labelledby="goal-detail-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Goal detail</p>
          <h1 id="goal-detail-title" className="mt-2 text-3xl font-semibold tracking-tight">
            {goal?.name ?? "Goal"}
          </h1>
          {goal?.description ? (
            <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">{goal.description}</p>
          ) : null}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Link href="/app/goals" className={buttonStyles({ variant: "outline", size: "sm" })}>
            Back to goals
          </Link>
          {deleteMutation.isSuccess ? null : (
            <Button
              type="button"
              size="sm"
              variant="ghost"
              className="text-red-600 hover:text-red-700 dark:text-red-400"
              onClick={() => deleteMutation.mutate()}
            >
              Delete goal
            </Button>
          )}
        </div>
      </div>

      <QueryState query={goalQuery} loadingLabel="Loading goal…">
        {!goal ? (
        <p className="rounded-[var(--radius)] border border-border bg-card p-4 text-sm text-muted-foreground">
          Goal not found.
        </p>
      ) : (
        <>
          <section className="rounded-[var(--radius)] border border-border bg-card p-5 shadow-sm">
            <div className="flex items-center justify-between gap-3">
              <h2 className="text-sm font-semibold">Progress</h2>
              <span className="text-2xl font-semibold tracking-tight">{goal.percentComplete}%</span>
            </div>
            <div className="mt-3 h-3 rounded-full bg-muted">
              <div
                className="h-3 rounded-full bg-primary"
                style={{ width: `${Math.min(100, Math.max(2, goal.percentComplete))}%` }}
              />
            </div>
            <p className="mt-2 text-sm text-muted-foreground">
              {goal.keyResultCount > 0
                ? `Average of ${goal.keyResultCount} key result${goal.keyResultCount === 1 ? "" : "s"}`
                : goal.targetType === "Numeric"
                  ? `${formatGoalValue(goal.currentValue ?? 0, goal.unit)} of ${formatGoalValue(goal.targetValue ?? 0, goal.unit)}`
                  : `${goal.completedLinkedTaskCount} of ${goal.linkedTaskCount} linked tasks completed`}
            </p>

            {goal.targetType === "Numeric" && goal.keyResultCount === 0 ? (
              <form
                className="mt-4 flex items-center gap-2"
                onSubmit={(event: FormEvent<HTMLFormElement>) => {
                  event.preventDefault();
                  const value = Number(currentValueDraft);
                  if (!Number.isFinite(value)) return;
                  updateValueMutation.mutate(value);
                }}
              >
                <label className="sr-only" htmlFor="current-value">
                  Current value
                </label>
                <input
                  id="current-value"
                  type="number"
                  placeholder={String(goal.currentValue ?? 0)}
                  value={currentValueDraft}
                  onChange={(event) => setCurrentValueDraft(event.target.value)}
                  className="h-9 w-40 rounded-lg border border-border bg-background px-3 text-sm"
                />
                <Button type="submit" size="sm" disabled={updateValueMutation.isPending}>
                  Update current value
                </Button>
              </form>
            ) : null}
          </section>

          <section className="rounded-[var(--radius)] border border-border bg-card shadow-sm">
            <header className="border-b border-border p-4">
              <h2 className="text-sm font-semibold">Key results</h2>
              <p className="mt-1 text-xs text-muted-foreground">
                Add multiple weighted key results to track progress OKR-style — once any exist, they
                replace the single current/target value above as the goal&apos;s overall progress.
              </p>
            </header>
            <ul className="divide-y divide-border">
              {goalQuery.data!.keyResults.length === 0 ? (
                <li className="p-4 text-sm text-muted-foreground">No key results yet.</li>
              ) : (
                goalQuery.data!.keyResults.map((kr) =>
                  editingKrId === kr.id ? (
                    <li key={kr.id} className="flex flex-wrap items-center gap-2 p-4 text-sm">
                      <input
                        aria-label="Key result title"
                        value={editDraft.title}
                        onChange={(event) => setEditDraft((d) => ({ ...d, title: event.target.value }))}
                        className="h-9 min-w-[10rem] flex-1 rounded-lg border border-border bg-background px-3 text-sm"
                      />
                      <input
                        aria-label="Current value"
                        type="number"
                        value={editDraft.current}
                        onChange={(event) => setEditDraft((d) => ({ ...d, current: event.target.value }))}
                        className="h-9 w-24 rounded-lg border border-border bg-background px-3 text-sm"
                      />
                      <input
                        aria-label="Target value"
                        type="number"
                        value={editDraft.target}
                        onChange={(event) => setEditDraft((d) => ({ ...d, target: event.target.value }))}
                        className="h-9 w-24 rounded-lg border border-border bg-background px-3 text-sm"
                      />
                      <select
                        aria-label="Unit"
                        value={editDraft.unit}
                        onChange={(event) => setEditDraft((d) => ({ ...d, unit: event.target.value as GoalUnit }))}
                        className="h-9 rounded-lg border border-border bg-background px-2 text-sm"
                      >
                        <option value="Number">Number</option>
                        <option value="Currency">Currency</option>
                        <option value="Percent">Percent</option>
                      </select>
                      <Button
                        type="button"
                        size="sm"
                        disabled={updateKeyResultMutation.isPending}
                        onClick={() => updateKeyResultMutation.mutate(kr.id)}
                      >
                        Save
                      </Button>
                      <Button type="button" size="sm" variant="ghost" onClick={() => setEditingKrId(null)}>
                        Cancel
                      </Button>
                    </li>
                  ) : (
                    <li key={kr.id} className="flex items-center justify-between gap-3 p-4 text-sm">
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center justify-between gap-2">
                          <span className="truncate font-medium">{kr.title}</span>
                          <span className="text-xs text-muted-foreground">{kr.percentComplete}%</span>
                        </div>
                        <p className="mt-1 text-xs text-muted-foreground">
                          {formatGoalValue(kr.currentValue, kr.unit)} of {formatGoalValue(kr.targetValue, kr.unit)}
                        </p>
                        <div className="mt-2 h-1.5 rounded-full bg-muted">
                          <div
                            className="h-1.5 rounded-full bg-primary"
                            style={{ width: `${Math.min(100, Math.max(2, kr.percentComplete))}%` }}
                          />
                        </div>
                      </div>
                      <div className="flex shrink-0 items-center gap-1">
                        <Button type="button" size="sm" variant="ghost" onClick={() => startEditingKeyResult(kr)}>
                          Edit
                        </Button>
                        <Button
                          type="button"
                          size="sm"
                          variant="ghost"
                          onClick={() => removeKeyResultMutation.mutate(kr.id)}
                        >
                          Remove
                        </Button>
                      </div>
                    </li>
                  ),
                )
              )}
            </ul>
            <form
              className="flex flex-wrap items-center gap-2 border-t border-border p-4"
              onSubmit={(event: FormEvent<HTMLFormElement>) => {
                event.preventDefault();
                if (!krTitle.trim()) return;
                addKeyResultMutation.mutate();
              }}
            >
              <label className="sr-only" htmlFor="kr-title">
                Key result title
              </label>
              <input
                id="kr-title"
                placeholder="Key result title…"
                value={krTitle}
                onChange={(event) => setKrTitle(event.target.value)}
                className="h-9 min-w-[10rem] flex-1 rounded-lg border border-border bg-background px-3 text-sm"
              />
              <label className="sr-only" htmlFor="kr-current">
                Current value
              </label>
              <input
                id="kr-current"
                type="number"
                placeholder="Current"
                value={krCurrent}
                onChange={(event) => setKrCurrent(event.target.value)}
                className="h-9 w-24 rounded-lg border border-border bg-background px-3 text-sm"
              />
              <label className="sr-only" htmlFor="kr-target">
                Target value
              </label>
              <input
                id="kr-target"
                type="number"
                placeholder="Target"
                value={krTarget}
                onChange={(event) => setKrTarget(event.target.value)}
                className="h-9 w-24 rounded-lg border border-border bg-background px-3 text-sm"
              />
              <select
                aria-label="Key result unit"
                value={krUnit}
                onChange={(event) => setKrUnit(event.target.value as GoalUnit)}
                className="h-9 rounded-lg border border-border bg-background px-2 text-sm"
              >
                <option value="Number">Number</option>
                <option value="Currency">Currency</option>
                <option value="Percent">Percent</option>
              </select>
              <Button type="submit" size="sm" disabled={addKeyResultMutation.isPending}>
                Add key result
              </Button>
            </form>
          </section>

          <section className="rounded-[var(--radius)] border border-border bg-card shadow-sm">
            <header className="border-b border-border p-4">
              <h2 className="text-sm font-semibold">Linked tasks</h2>
              <p className="mt-1 text-xs text-muted-foreground">
                Tasks you cannot read (private, no grant) show as &ldquo;Restricted&rdquo; — their title is
                never sent to the browser.
              </p>
            </header>
            <ul className="divide-y divide-border">
              {goalQuery.data!.linkedTasks.length === 0 ? (
                <li className="p-4 text-sm text-muted-foreground">No linked tasks yet.</li>
              ) : (
                goalQuery.data!.linkedTasks.map((task) => (
                  <li key={task.taskId} className="flex items-center justify-between gap-3 p-4 text-sm">
                    <div>
                      {task.visible ? (
                        <>
                          <span>{task.title}</span>
                          {task.isCompleted ? (
                            <span className="ml-2 rounded-full bg-emerald-500/10 px-2 py-0.5 text-xs font-medium text-emerald-700 dark:text-emerald-400">
                              Done
                            </span>
                          ) : null}
                        </>
                      ) : (
                        <span className="italic text-muted-foreground">Restricted task (no access)</span>
                      )}
                    </div>
                    <Button
                      type="button"
                      size="sm"
                      variant="ghost"
                      onClick={() => unlinkMutation.mutate(task.taskId)}
                    >
                      Unlink
                    </Button>
                  </li>
                ))
              )}
            </ul>
            {goal.targetType === "LinkedTasksRatio" ? (
              <form
                className="flex items-center gap-2 border-t border-border p-4"
                onSubmit={(event: FormEvent<HTMLFormElement>) => {
                  event.preventDefault();
                  if (!taskIdDraft.trim()) return;
                  linkMutation.mutate(taskIdDraft.trim());
                }}
              >
                <label className="sr-only" htmlFor="link-task-id">
                  Task to link
                </label>
                <div className="flex-1">
                  <ResourcePicker
                    id="link-task-id"
                    types={["Task"]}
                    value={taskIdDraft}
                    onChange={(id) => setTaskIdDraft(id)}
                    placeholder="Search tasks…"
                  />
                </div>
                <Button type="submit" size="sm" disabled={linkMutation.isPending}>
                  Link task
                </Button>
              </form>
            ) : null}
          </section>

          <section className="rounded-[var(--radius)] border border-border bg-card shadow-sm">
            <header className="border-b border-border p-4">
              <h2 className="text-sm font-semibold">Comments</h2>
            </header>
            <ul className="divide-y divide-border">
              {(commentsQuery.data ?? []).length === 0 ? (
                <li className="p-4 text-sm text-muted-foreground">No comments yet.</li>
              ) : (
                commentsQuery.data!.map((comment) => (
                  <li key={comment.id} className="p-4 text-sm">
                    <p>{comment.body}</p>
                    <p className="mt-1 text-xs text-muted-foreground">
                      {new Date(comment.createdAtUtc).toLocaleString()}
                    </p>
                  </li>
                ))
              )}
            </ul>
            <form
              className="flex items-center gap-2 border-t border-border p-4"
              onSubmit={(event: FormEvent<HTMLFormElement>) => {
                event.preventDefault();
                if (!commentDraft.trim()) return;
                commentMutation.mutate(commentDraft.trim());
              }}
            >
              <label className="sr-only" htmlFor="comment-body">
                Add a comment
              </label>
              <input
                id="comment-body"
                placeholder="Add a comment…"
                value={commentDraft}
                onChange={(event) => setCommentDraft(event.target.value)}
                className="h-9 flex-1 rounded-lg border border-border bg-background px-3 text-sm"
              />
              <Button type="submit" size="sm" disabled={commentMutation.isPending}>
                Post
              </Button>
            </form>
          </section>
        </>
      )}
      </QueryState>
    </section>
  );
}
