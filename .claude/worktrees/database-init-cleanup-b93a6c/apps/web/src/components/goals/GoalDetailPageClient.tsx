"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import type { FormEvent } from "react";
import { Button, buttonStyles } from "@/components/ui/Button";
import {
  addGoalComment,
  deleteGoal,
  getGoal,
  linkGoalTask,
  listGoalComments,
  unlinkGoalTask,
  updateGoal,
} from "@/lib/goals/client";
import { goalKeys } from "@/lib/goals/queries";

export function GoalDetailPageClient({ goalId }: { goalId: string }) {
  const queryClient = useQueryClient();
  const [taskIdDraft, setTaskIdDraft] = useState("");
  const [commentDraft, setCommentDraft] = useState("");
  const [currentValueDraft, setCurrentValueDraft] = useState("");

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

      {goalQuery.isLoading || !goal ? (
        <p className="rounded-[var(--radius)] border border-border bg-card p-4 text-sm text-muted-foreground">
          {goalQuery.isLoading ? "Loading goal…" : "Goal not found."}
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
              {goal.targetType === "Numeric"
                ? `${goal.currentValue ?? 0} of ${goal.targetValue ?? 0}`
                : `${goal.completedLinkedTaskCount} of ${goal.linkedTaskCount} linked tasks completed`}
            </p>

            {goal.targetType === "Numeric" ? (
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
                  Task id to link
                </label>
                <input
                  id="link-task-id"
                  placeholder="Task id"
                  value={taskIdDraft}
                  onChange={(event) => setTaskIdDraft(event.target.value)}
                  className="h-9 flex-1 rounded-lg border border-border bg-background px-3 text-sm"
                />
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
    </section>
  );
}
