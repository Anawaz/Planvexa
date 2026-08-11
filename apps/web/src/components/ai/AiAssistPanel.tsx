"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/Button";
import { TaskSelect } from "@/components/work/TaskSelect";
import { getAiFeatureStatus, getAiUsage, suggestPriority, suggestSubtasks, summarizeTask } from "@/lib/ai/client";
import { aiKeys } from "@/lib/ai/queries";
import type { AiPrioritySuggestion, AiSubtaskSuggestion, AiSummary } from "@/lib/ai/types";
import { getTask } from "@/lib/work/client";
import { createTaskOffline } from "@/lib/work/offlineMutations";
import { useWorkMutation } from "@/lib/work/mutations";
import { workKeys } from "@/lib/work/queries";
import { cn } from "@/lib/utils";

const numberFormatter = new Intl.NumberFormat("en");
const panelClassName = "rounded-[var(--radius)] border border-border bg-card shadow-sm";

const priorityClasses: Record<string, string> = {
  None: "bg-muted text-muted-foreground",
  Low: "bg-sky-100 text-sky-800 dark:bg-sky-950 dark:text-sky-200",
  Normal: "bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-200",
  High: "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200",
  Urgent: "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200",
};

function TokenEstimate({ tokens }: { tokens: number }) {
  return (
    <p className="text-xs font-medium text-muted-foreground">
      {numberFormatter.format(tokens)} estimated tokens
    </p>
  );
}

function ResultShell({ title, children }: { title: string; children: ReactNode }) {
  const titleId = `${title.toLowerCase().replaceAll(" ", "-")}-title`;

  return (
    <section className="rounded-xl border border-border bg-background p-4" aria-labelledby={titleId}>
      <h3 id={titleId} className="text-sm font-semibold">
        {title}
      </h3>
      <div className="mt-3 space-y-3">{children}</div>
    </section>
  );
}

export function AiAssistPanel() {
  const queryClient = useQueryClient();
  const [taskId, setTaskId] = useState("");
  const [summary, setSummary] = useState<AiSummary | null>(null);
  const [subtasks, setSubtasks] = useState<AiSubtaskSuggestion | null>(null);
  const [priority, setPriority] = useState<AiPrioritySuggestion | null>(null);
  const [selectedSubtasks, setSelectedSubtasks] = useState<string[]>([]);
  const [addedSubtasks, setAddedSubtasks] = useState<string[]>([]);

  const featureStatusQuery = useQuery({ queryKey: aiKeys.featureStatus(), queryFn: getAiFeatureStatus });
  const aiEnabled = featureStatusQuery.data?.enabled ?? true;
  const usageQuery = useQuery({ queryKey: aiKeys.usage(), queryFn: getAiUsage, enabled: aiEnabled });
  // The task's listId (needed to create a subtask under it) isn't part of the AI suggestion
  // response, so it's fetched straight from the task itself.
  const taskQuery = useQuery({
    queryKey: workKeys.task(taskId),
    queryFn: () => getTask(taskId),
    enabled: taskId.trim().length > 0,
  });

  const addSubtaskMutation = useWorkMutation(createTaskOffline);

  const summarizeMutation = useMutation({
    mutationFn: () => summarizeTask(taskId),
    onSuccess: (result) => {
      setSummary(result);
      void queryClient.invalidateQueries({ queryKey: aiKeys.usage() });
    },
  });

  const subtasksMutation = useMutation({
    mutationFn: () => suggestSubtasks(taskId),
    onSuccess: (result) => {
      setSubtasks(result);
      setSelectedSubtasks([]);
      setAddedSubtasks([]);
      void queryClient.invalidateQueries({ queryKey: aiKeys.usage() });
    },
  });

  const priorityMutation = useMutation({
    mutationFn: () => suggestPriority(taskId),
    onSuccess: (result) => {
      setPriority(result);
      void queryClient.invalidateQueries({ queryKey: aiKeys.usage() });
    },
  });

  function toggleSubtask(title: string, checked: boolean) {
    setSelectedSubtasks((current) =>
      checked ? [...current, title] : current.filter((selectedTitle) => selectedTitle !== title),
    );
  }

  async function handleAddSelected() {
    const listId = taskQuery.data?.listId;
    if (!listId || selectedSubtasks.length === 0) return;

    const titles = selectedSubtasks;
    await Promise.all(
      titles.map((title) => addSubtaskMutation.mutateAsync({ listId, parentId: taskId, title })),
    );
    setAddedSubtasks((current) => [...current, ...titles]);
    setSelectedSubtasks([]);
  }

  const hasTaskId = taskId.trim().length > 0;
  const isBusy = summarizeMutation.isPending || subtasksMutation.isPending || priorityMutation.isPending;
  const actionsDisabled = !hasTaskId || isBusy || !aiEnabled;
  const usage = usageQuery.data;

  return (
    <section aria-labelledby="ai-assist-title" className="space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">AI, Mobile & Retention</p>
        <h1 id="ai-assist-title" className="mt-2 text-3xl font-semibold tracking-tight">
          AI Assist
        </h1>
        <p className="mt-3 max-w-3xl text-sm leading-6 text-muted-foreground">
          Task summaries, subtask suggestions, and priority recommendations from the workspace AI
          endpoints. Usage is metered per workspace.
        </p>
      </div>

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <section className={cn(panelClassName, "space-y-5 p-5")} aria-labelledby="ai-actions-title">
          <div>
            <h2 id="ai-actions-title" className="text-lg font-semibold">
              Task prompt
            </h2>
            <p className="mt-1 text-sm text-muted-foreground">
              Choose a task from this workspace.
            </p>
          </div>

          <div className="grid gap-2">
            <label htmlFor="ai-task-id" className="text-sm font-medium">Task</label>
            <TaskSelect id="ai-task-id" value={taskId} onChange={setTaskId} />
            <p className="text-xs text-muted-foreground">Results stay in local UI state; only usage is persisted.</p>
          </div>

          <div className="flex flex-wrap gap-3" aria-label="AI task actions">
            <Button type="button" disabled={actionsDisabled} onClick={() => summarizeMutation.mutate()}>
              Summarize
            </Button>
            <Button
              type="button"
              variant="secondary"
              disabled={actionsDisabled}
              onClick={() => subtasksMutation.mutate()}
            >
              Suggest subtasks
            </Button>
            <Button
              type="button"
              variant="outline"
              disabled={actionsDisabled}
              onClick={() => priorityMutation.mutate()}
            >
              Suggest priority
            </Button>
          </div>

          {!aiEnabled ? (
            <p
              role="alert"
              className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-200"
            >
              AI has been disabled for this workspace. Ask a workspace admin to re-enable it under AI provider
              settings.
            </p>
          ) : !hasTaskId ? (
            <p
              role="alert"
              className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-200"
            >
              Enter a task ID before running an AI action.
            </p>
          ) : null}

          <div className="grid gap-4" aria-live="polite">
            {summary ? (
              <ResultShell title="Summary">
                <p className="text-sm leading-6 text-muted-foreground">{summary.summary}</p>
                <TokenEstimate tokens={summary.tokensEstimated} />
              </ResultShell>
            ) : null}

            {subtasks ? (
              <ResultShell title="Suggested subtasks">
                <fieldset className="space-y-2">
                  <legend className="sr-only">Subtasks to add</legend>
                  {subtasks.titles.map((title, index) => {
                    const inputId = `ai-subtask-${index}`;
                    const isAdded = addedSubtasks.includes(title);

                    return (
                      <label
                        key={title}
                        htmlFor={inputId}
                        className="flex items-start gap-3 rounded-lg border border-border bg-card p-3 text-sm"
                      >
                        <input
                          id={inputId}
                          type="checkbox"
                          checked={selectedSubtasks.includes(title)}
                          disabled={isAdded}
                          onChange={(event) => toggleSubtask(title, event.target.checked)}
                          className="mt-0.5 size-4 rounded border-border accent-[var(--primary)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                        />
                        <span>
                          <span className="block font-medium">{title}</span>
                          {isAdded ? (
                            <span className="mt-1 block text-xs text-emerald-700 dark:text-emerald-300">
                              Added as a subtask.
                            </span>
                          ) : null}
                        </span>
                      </label>
                    );
                  })}
                </fieldset>
                <div className="flex flex-wrap items-center gap-3">
                  <Button
                    type="button"
                    size="sm"
                    disabled={selectedSubtasks.length === 0 || !taskQuery.data || addSubtaskMutation.isPending}
                    onClick={() => void handleAddSelected()}
                  >
                    {addSubtaskMutation.isPending
                      ? "Adding…"
                      : `Add selected (${numberFormatter.format(selectedSubtasks.length)})`}
                  </Button>
                  {addedSubtasks.length > 0 ? (
                    <p className="text-xs font-medium text-emerald-700 dark:text-emerald-300">
                      Added {numberFormatter.format(addedSubtasks.length)} subtask
                      {addedSubtasks.length === 1 ? "" : "s"} to this task.
                    </p>
                  ) : null}
                </div>
                <TokenEstimate tokens={subtasks.tokensEstimated} />
              </ResultShell>
            ) : null}

            {priority ? (
              <ResultShell title="Priority suggestion">
                <span
                  className={cn(
                    "inline-flex rounded-full px-3 py-1 text-xs font-semibold",
                    priorityClasses[priority.priority] ?? priorityClasses.None,
                  )}
                >
                  {priority.priority}
                </span>
                <p className="text-sm leading-6 text-muted-foreground">{priority.rationale}</p>
                <TokenEstimate tokens={priority.tokensEstimated} />
              </ResultShell>
            ) : null}
          </div>
        </section>

        <aside className={cn(panelClassName, "h-fit space-y-4 p-5")} aria-labelledby="ai-usage-title">
          <div>
            <h2 id="ai-usage-title" className="text-lg font-semibold">
              Usage and credits
            </h2>
            <p className="mt-1 text-sm text-muted-foreground">Tenant-level AI metering.</p>
          </div>

          {usageQuery.isLoading || !usage ? (
            <p className="text-sm text-muted-foreground">Loading AI usage…</p>
          ) : (
            <>
              <dl className="grid gap-3">
                <div className="rounded-xl border border-border bg-background p-3">
                  <dt className="text-xs text-muted-foreground">Requests</dt>
                  <dd className="mt-1 text-2xl font-semibold">{numberFormatter.format(usage.requestCount)}</dd>
                </div>
                <div className="rounded-xl border border-border bg-background p-3">
                  <dt className="text-xs text-muted-foreground">Estimated tokens</dt>
                  <dd className="mt-1 text-2xl font-semibold">{numberFormatter.format(usage.tokensEstimated)}</dd>
                </div>
                <div className="rounded-xl border border-border bg-background p-3">
                  <dt className="text-xs text-muted-foreground">Credit limit</dt>
                  <dd className="mt-1 text-lg font-semibold">
                    {usage.creditLimit == null ? "Not set" : numberFormatter.format(usage.creditLimit)}
                  </dd>
                </div>
              </dl>

              {usage.creditsEnabled ? (
                <p className="rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-sm font-medium text-emerald-800 dark:border-emerald-900 dark:bg-emerald-950 dark:text-emerald-200">
                  AI credits enabled for this workspace.
                </p>
              ) : (
                <div className="rounded-lg border border-dashed border-amber-300 bg-amber-50 p-4 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100">
                  <p className="font-semibold">AI credits disabled — upgrade your plan</p>
                  <p className="mt-1 leading-5">Requests still run, but no credit allowance is applied.</p>
                </div>
              )}
            </>
          )}
        </aside>
      </div>
    </section>
  );
}
