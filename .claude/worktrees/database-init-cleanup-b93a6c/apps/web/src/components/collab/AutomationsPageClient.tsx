"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import type { FormEvent } from "react";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { MemberSelect } from "@/components/people/MemberSelect";
import {
  createAutomation,
  deleteAutomation,
  getAutomationRuns,
  listAutomations,
  setAutomationEnabled,
  updateAutomation,
} from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import { AUTOMATION_TRIGGER_TYPES, type AutomationRule } from "@/lib/collab/types";
import { useAppContext } from "@/lib/app-context/AppContext";
import { listStatusSchemes, listTags } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";
import { cn } from "@/lib/utils";
import {
  formatIsoDateTime,
  numberFormatter,
  panelClassName,
  textInputClassName,
} from "./collab-ui";

const triggerTypes = AUTOMATION_TRIGGER_TYPES;
const conditionFields = ["status", "list", "assignee"] as const;
const actionTypes = ["set_status", "assign", "add_tag", "notify"] as const;

type ConditionField = (typeof conditionFields)[number];
type ActionType = (typeof actionTypes)[number];

type RuleDraft = {
  name: string;
  triggerType: string;
  isEnabled: boolean;
  conditionField: ConditionField;
  conditionValue: string;
  actionType: ActionType;
  actionValue: string;
};

function parseJsonObject(value: string): Record<string, string> {
  try {
    const parsed = JSON.parse(value) as Record<string, unknown>;
    return Object.fromEntries(
      Object.entries(parsed).map(([key, item]) => [key, typeof item === "string" ? item : String(item)]),
    );
  } catch {
    return {};
  }
}

function draftFromRule(rule: AutomationRule): RuleDraft {
  const condition = parseJsonObject(rule.conditionJson);
  const action = parseJsonObject(rule.actionJson);
  const conditionField = conditionFields.includes(condition.field as ConditionField)
    ? (condition.field as ConditionField)
    : "status";
  const actionType = actionTypes.includes(action.type as ActionType) ? (action.type as ActionType) : "notify";

  return {
    name: rule.name,
    triggerType: rule.triggerType,
    isEnabled: rule.isEnabled,
    conditionField,
    conditionValue: condition.equals ?? "",
    actionType,
    actionValue: action.value ?? "",
  };
}

function conditionJson(draft: RuleDraft) {
  return JSON.stringify({ field: draft.conditionField, equals: draft.conditionValue });
}

function actionJson(draft: RuleDraft) {
  return JSON.stringify({ type: draft.actionType, value: draft.actionValue });
}

type ValueKind = "member" | "status" | "tag" | "text";

function conditionValueKind(field: ConditionField): ValueKind {
  if (field === "assignee") return "member";
  if (field === "status") return "status";
  return "text";
}

function actionValueKind(action: ActionType): ValueKind {
  if (action === "assign") return "member";
  if (action === "set_status") return "status";
  if (action === "add_tag") return "tag";
  return "text";
}

/**
 * Renders the correct picker for an automation value so builders never paste a raw tag/status/user
 * GUID: member → {@link MemberSelect}, status/tag → workspace-scoped selects, otherwise free text.
 */
function AutomationValueField({
  kind,
  value,
  onChange,
  className,
  textPlaceholder,
}: {
  kind: ValueKind;
  value: string;
  onChange: (value: string) => void;
  className?: string;
  textPlaceholder?: string;
}) {
  const tagsQuery = useQuery({ queryKey: workKeys.tags(), queryFn: listTags, enabled: kind === "tag" });
  const statusQuery = useQuery({ queryKey: workKeys.statusSchemes(), queryFn: listStatusSchemes, enabled: kind === "status" });

  if (kind === "member") {
    return <MemberSelect value={value} onChange={onChange} includeAny anyLabel="Select a member…" className={className} />;
  }

  if (kind === "tag") {
    const tags = tagsQuery.data ?? [];
    return (
      <select value={value} onChange={(event) => onChange(event.target.value)} className={className}>
        <option value="">Select a tag…</option>
        {tags.map((tag) => (
          <option key={tag.id} value={tag.id}>{tag.name}</option>
        ))}
      </select>
    );
  }

  if (kind === "status") {
    const seen = new Set<string>();
    const statuses = (statusQuery.data ?? [])
      .flatMap((scheme) => scheme.statuses)
      .filter((status) => (seen.has(status.id) ? false : (seen.add(status.id), true)));
    return (
      <select value={value} onChange={(event) => onChange(event.target.value)} className={className}>
        <option value="">Select a status…</option>
        {statuses.map((status) => (
          <option key={status.id} value={status.id}>{status.name}</option>
        ))}
      </select>
    );
  }

  return (
    <input
      value={value}
      onChange={(event) => onChange(event.target.value)}
      className={className}
      placeholder={textPlaceholder ?? "Value"}
    />
  );
}

function runStatusClassName(status: string) {
  if (status === "Success") {
    return "bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200";
  }

  if (status === "Failed") {
    return "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200";
  }

  return "bg-muted text-muted-foreground";
}

function RuleEditor({ rule, onDeleted }: { rule: AutomationRule; onDeleted: () => void }) {
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  const [draft, setDraft] = useState<RuleDraft>(() => draftFromRule(rule));
  const saveMutation = useMutation({
    // Enablement is a separate endpoint on the API; PATCH only carries the definition.
    mutationFn: async ({ id, value }: { id: string; value: RuleDraft }) => {
      const saved = await updateAutomation(id, {
        name: value.name,
        triggerType: value.triggerType,
        conditionJson: conditionJson(value),
        actionJson: actionJson(value),
      });
      return value.isEnabled === saved.isEnabled ? saved : setAutomationEnabled(id, value.isEnabled);
    },
    onSuccess: (savedRule) => {
      setDraft(draftFromRule(savedRule));
      void queryClient.invalidateQueries({ queryKey: collabKeys.automationsRoot(workspaceId) });
    },
  });
  const deleteMutation = useMutation({
    mutationFn: deleteAutomation,
    onSuccess: () => {
      onDeleted();
      void queryClient.invalidateQueries({ queryKey: collabKeys.automationsRoot(workspaceId) });
    },
  });

  function saveRule(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    saveMutation.mutate({ id: rule.id, value: draft });
  }

  return (
    <form onSubmit={saveRule} className={cn(panelClassName, "p-4")}>
      <div className="flex flex-col gap-3 border-b border-border pb-4 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <h2 className="text-lg font-semibold">Rule editor</h2>
          <p className="mt-1 text-xs text-muted-foreground">
            Builders render to conditionJson and actionJson strings.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={deleteMutation.isPending}
            onClick={() => deleteMutation.mutate(rule.id)}
          >
            Delete
          </Button>
          <Button type="submit" size="sm" disabled={saveMutation.isPending}>
            Save rule
          </Button>
        </div>
      </div>

      <div className="mt-4 grid gap-4 lg:grid-cols-2">
        <label className="grid gap-1 text-xs font-medium">
          Name
          <input
            value={draft.name}
            onChange={(event) => setDraft({ ...draft, name: event.target.value })}
            className={textInputClassName}
          />
        </label>
        <label className="grid gap-1 text-xs font-medium">
          Trigger type
          <select
            value={draft.triggerType}
            onChange={(event) => setDraft({ ...draft, triggerType: event.target.value })}
            className={textInputClassName}
          >
            {/* Keep an unrecognised stored trigger selectable instead of silently rewriting it. */}
            {[...new Set<string>([draft.triggerType, ...triggerTypes])].filter(Boolean).map((trigger) => (
              <option key={trigger} value={trigger}>
                {trigger}
              </option>
            ))}
          </select>
        </label>
        <fieldset className="rounded-xl border border-border bg-background p-3">
          <legend className="px-1 text-xs font-semibold">Condition builder</legend>
          <div className="mt-2 grid gap-3 sm:grid-cols-2">
            <label className="grid gap-1 text-xs font-medium">
              Match field
              <select
                value={draft.conditionField}
                onChange={(event) => setDraft({ ...draft, conditionField: event.target.value as ConditionField })}
                className={textInputClassName}
              >
                {conditionFields.map((field) => (
                  <option key={field} value={field}>
                    {field}
                  </option>
                ))}
              </select>
            </label>
            <label className="grid gap-1 text-xs font-medium">
              Equals
              <AutomationValueField
                kind={conditionValueKind(draft.conditionField)}
                value={draft.conditionValue}
                onChange={(value) => setDraft({ ...draft, conditionValue: value })}
                className={textInputClassName}
                textPlaceholder="In review"
              />
            </label>
          </div>
        </fieldset>
        <fieldset className="rounded-xl border border-border bg-background p-3">
          <legend className="px-1 text-xs font-semibold">Action builder</legend>
          <div className="mt-2 grid gap-3 sm:grid-cols-2">
            <label className="grid gap-1 text-xs font-medium">
              Action
              <select
                value={draft.actionType}
                onChange={(event) => setDraft({ ...draft, actionType: event.target.value as ActionType })}
                className={textInputClassName}
              >
                {actionTypes.map((action) => (
                  <option key={action} value={action}>
                    {action}
                  </option>
                ))}
              </select>
            </label>
            <label className="grid gap-1 text-xs font-medium">
              Value
              <AutomationValueField
                kind={actionValueKind(draft.actionType)}
                value={draft.actionValue}
                onChange={(value) => setDraft({ ...draft, actionValue: value })}
                className={textInputClassName}
                textPlaceholder="Notification message"
              />
            </label>
          </div>
        </fieldset>
        <label className="flex items-center gap-2 text-sm lg:col-span-2">
          <input
            type="checkbox"
            checked={draft.isEnabled}
            onChange={(event) => setDraft({ ...draft, isEnabled: event.target.checked })}
            className="size-4 rounded border-border accent-[var(--primary)]"
          />
          Rule is enabled
        </label>
      </div>

      <div className="mt-4 grid gap-3 lg:grid-cols-2">
        <div className="rounded-xl border border-border bg-background p-3">
          <p className="text-xs font-semibold text-muted-foreground">conditionJson</p>
          <code className="mt-2 block break-all text-xs">{conditionJson(draft)}</code>
        </div>
        <div className="rounded-xl border border-border bg-background p-3">
          <p className="text-xs font-semibold text-muted-foreground">actionJson</p>
          <code className="mt-2 block break-all text-xs">{actionJson(draft)}</code>
        </div>
      </div>
    </form>
  );
}

export function AutomationsPageClient() {
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  const [selectedRuleId, setSelectedRuleId] = useState<string | null>(null);
  const [newRuleName, setNewRuleName] = useState("");
  const automationsQuery = useQuery({
    queryKey: collabKeys.automations(workspaceId),
    queryFn: listAutomations,
  });
  const automations = automationsQuery.data ?? [];
  const activeRuleId = selectedRuleId ?? automations[0]?.id ?? "";
  const activeRule = automations.find((rule) => rule.id === activeRuleId);
  const runsQuery = useQuery({
    queryKey: collabKeys.automationRuns(workspaceId, activeRuleId),
    queryFn: () => getAutomationRuns(activeRuleId),
    enabled: Boolean(activeRuleId),
  });
  const createMutation = useMutation({
    mutationFn: createAutomation,
    onSuccess: (rule) => {
      setNewRuleName("");
      setSelectedRuleId(rule.id);
      void queryClient.invalidateQueries({ queryKey: collabKeys.automationsRoot(workspaceId) });
    },
  });
  const enableMutation = useMutation({
    mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) => setAutomationEnabled(id, enabled),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: collabKeys.automationsRoot(workspaceId) });
    },
  });

  function submitNewRule(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const value: RuleDraft = {
      name: newRuleName.trim() || "Untitled automation",
      triggerType: "task.created",
      isEnabled: true,
      conditionField: "status",
      conditionValue: "",
      actionType: "notify",
      actionValue: "",
    };

    createMutation.mutate({
      name: value.name,
      triggerType: value.triggerType,
      conditionJson: conditionJson(value),
      actionJson: actionJson(value),
    });
  }

  const runs = runsQuery.data ?? [];

  return (
    <section aria-labelledby="automations-title" className="space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">Automations</p>
        <h1 id="automations-title" className="mt-2 text-3xl font-semibold tracking-tight">
          Automations
        </h1>
        <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
          Compose trigger, condition, and action JSON; the API executes matching rules on workspace events.
        </p>
      </div>

      <div className="grid gap-6 xl:grid-cols-[22rem_1fr]">
        <aside className="space-y-4">
          <section className={cn(panelClassName, "p-4")} aria-labelledby="automation-list-title">
            <h2 id="automation-list-title" className="text-sm font-semibold">
              Rules
            </h2>
            <div className="mt-3 space-y-2">
              {automationsQuery.isLoading ? (
                <p className="text-sm text-muted-foreground">Loading automations…</p>
              ) : automations.length === 0 ? (
                <EmptyState
                  title="No automation rules yet"
                  description="Pick a trigger and an action in the rule builder on this page to let the workspace do the repetitive part."
                />
              ) : (
                automations.map((rule) => (
                  <div key={rule.id} className="rounded-xl border border-border bg-background p-3">
                    <button
                      type="button"
                      aria-pressed={activeRuleId === rule.id}
                      onClick={() => setSelectedRuleId(rule.id)}
                      className="w-full text-left focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                    >
                      <span className="block text-sm font-semibold">{rule.name}</span>
                      <span className="mt-1 block text-xs text-muted-foreground">{rule.triggerType}</span>
                    </button>
                    <div className="mt-3 flex items-center justify-between gap-2">
                      <span
                        className={cn(
                          "rounded-full px-2.5 py-1 text-xs font-semibold",
                          rule.isEnabled ? "bg-primary/10 text-primary" : "bg-muted text-muted-foreground",
                        )}
                      >
                        {rule.isEnabled ? "Enabled" : "Disabled"}
                      </span>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        disabled={enableMutation.isPending}
                        onClick={() => enableMutation.mutate({ id: rule.id, enabled: !rule.isEnabled })}
                      >
                        {rule.isEnabled ? "Disable" : "Enable"}
                      </Button>
                    </div>
                  </div>
                ))
              )}
            </div>
          </section>

          <form onSubmit={submitNewRule} className={cn(panelClassName, "p-4")}>
            <h2 className="text-sm font-semibold">Create automation</h2>
            <label className="mt-4 grid gap-1 text-xs font-medium">
              Rule name
              <input
                value={newRuleName}
                onChange={(event) => setNewRuleName(event.target.value)}
                className={textInputClassName}
                placeholder="Route urgent requests"
              />
            </label>
            <Button type="submit" size="sm" className="mt-3" disabled={createMutation.isPending}>
              Add automation
            </Button>
          </form>
        </aside>

        <div className="space-y-6">
          {activeRule ? (
            <RuleEditor key={activeRule.id} rule={activeRule} onDeleted={() => setSelectedRuleId(null)} />
          ) : (
            <section className={cn(panelClassName, "p-6 text-sm text-muted-foreground")}>
              Select or create an automation rule.
            </section>
          )}

          <section className={cn(panelClassName, "overflow-hidden")} aria-labelledby="automation-runs-title">
            <header className="border-b border-border p-4">
              <h2 id="automation-runs-title" className="text-sm font-semibold">
                Runs ({numberFormatter.format(runs.length)})
              </h2>
            </header>
            <div className="overflow-x-auto">
              <table className="min-w-full text-left text-sm">
                <thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground">
                  <tr>
                    <th className="px-4 py-3 font-semibold">Status</th>
                    <th className="px-4 py-3 font-semibold">Detail</th>
                    <th className="px-4 py-3 font-semibold">Time</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {runs.map((run) => (
                    <tr key={run.id}>
                      <td className="px-4 py-3">
                        <span className={cn("rounded-full px-2.5 py-1 text-xs font-semibold", runStatusClassName(run.status))}>
                          {run.status}
                        </span>
                      </td>
                      <td className="px-4 py-3">{run.detail ?? "No detail"}</td>
                      <td className="px-4 py-3 text-muted-foreground">{formatIsoDateTime(run.occurredAtUtc)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {runsQuery.isLoading ? (
                <p className="p-4 text-sm text-muted-foreground">Loading automation runs…</p>
              ) : null}
            </div>
          </section>
        </div>
      </div>
    </section>
  );
}
