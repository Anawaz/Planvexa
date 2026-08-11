"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FormEvent, useState } from "react";
import { Button } from "@/components/ui/Button";
import { getPolicy, listRates, setUserRate, updatePolicy } from "@/lib/time/client";
import { fromLocalDateTimeInputValue, toLocalDateTimeInputValue } from "@/lib/time/format";
import { timeKeys } from "@/lib/time/queries";
import type { MemberRate, TimePolicy } from "@/lib/time/types";
import { useMembers, type Member } from "@/lib/members";

type BooleanPolicyKey =
  | "singleActiveTimer"
  | "billableByDefault"
  | "requireDescription"
  | "requireTask"
  | "approvalRequired";

type NumericPolicyKey =
  | "roundingMinutes"
  | "minimumDurationSeconds"
  | "maximumEntrySeconds"
  | "editWindowHours"
  | "weekStartsOn"
  | "overtimeThresholdSeconds";

const booleanFields: Array<{ key: BooleanPolicyKey; label: string; help: string }> = [
  {
    key: "singleActiveTimer",
    label: "Single active timer",
    help: "Prevents users from running multiple timers at once.",
  },
  {
    key: "billableByDefault",
    label: "Billable by default",
    help: "New timers and manual entries start as billable.",
  },
  {
    key: "requireDescription",
    label: "Require description",
    help: "Entries should explain what work was performed.",
  },
  {
    key: "requireTask",
    label: "Require task",
    help: "Entries must be attached to a task before submission.",
  },
  {
    key: "approvalRequired",
    label: "Approval required",
    help: "Timesheets must be submitted and approved before locking.",
  },
];

const numericFields: Array<{
  key: NumericPolicyKey;
  label: string;
  min: number;
  step: number;
  suffix: string;
}> = [
  { key: "roundingMinutes", label: "Rounding", min: 0, step: 5, suffix: "minutes" },
  { key: "minimumDurationSeconds", label: "Minimum entry", min: 0, step: 60, suffix: "seconds" },
  { key: "maximumEntrySeconds", label: "Maximum entry", min: 60, step: 900, suffix: "seconds" },
  { key: "editWindowHours", label: "Edit window", min: 0, step: 1, suffix: "hours" },
  { key: "weekStartsOn", label: "Week starts on", min: 0, step: 1, suffix: "0 Sun · 1 Mon" },
  { key: "overtimeThresholdSeconds", label: "Overtime threshold", min: 0, step: 3600, suffix: "seconds/week" },
];

const errorClassName =
  "rounded-[var(--radius)] border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300";
const numberInputClassName =
  "min-w-0 flex-1 bg-transparent px-3 py-2 text-sm font-normal outline-none";

function memberLabel(member: Member) {
  return member.displayName || member.email || member.userId;
}

/** One editable rate row; local drafts so a slow save cannot clobber a neighbouring row. */
function RateRow({
  member,
  rate,
  isSaving,
  onSave,
}: {
  member: Member;
  rate: MemberRate | undefined;
  isSaving: boolean;
  onSave: (userId: string, values: { billingRate: number; costRate: number }) => void;
}) {
  const [billingRate, setBillingRate] = useState(String(rate?.billingRate ?? 0));
  const [costRate, setCostRate] = useState(String(rate?.costRate ?? 0));

  return (
    <tr className="border-t border-border">
      <td className="px-4 py-3">
        <span className="block text-sm font-medium">{memberLabel(member)}</span>
        <span className="block text-xs text-muted-foreground">{member.role}</span>
      </td>
      <td className="px-4 py-3">
        <label className="sr-only" htmlFor={`billing-${member.userId}`}>
          Billing rate for {memberLabel(member)}
        </label>
        <input
          id={`billing-${member.userId}`}
          type="number"
          min={0}
          step={5}
          value={billingRate}
          className="w-28 rounded-lg border border-border bg-background px-3 py-2 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          onChange={(event) => setBillingRate(event.target.value)}
        />
      </td>
      <td className="px-4 py-3">
        <label className="sr-only" htmlFor={`cost-${member.userId}`}>
          Cost rate for {memberLabel(member)}
        </label>
        <input
          id={`cost-${member.userId}`}
          type="number"
          min={0}
          step={5}
          value={costRate}
          className="w-28 rounded-lg border border-border bg-background px-3 py-2 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          onChange={(event) => setCostRate(event.target.value)}
        />
      </td>
      <td className="px-4 py-3 text-right">
        <Button
          type="button"
          size="sm"
          variant="outline"
          disabled={isSaving}
          onClick={() =>
            onSave(member.userId, {
              billingRate: Math.max(0, Number(billingRate) || 0),
              costRate: Math.max(0, Number(costRate) || 0),
            })
          }
        >
          Save rate
        </Button>
      </td>
    </tr>
  );
}

function BillingRatesCard() {
  const queryClient = useQueryClient();
  const membersQuery = useMembers();
  // Admin-only on the API (TimeAuthorizer.EnsureManage); a Member gets 403 and the notice below.
  const ratesQuery = useQuery({ queryKey: timeKeys.rates(), queryFn: listRates });
  const saveRate = useMutation({
    mutationFn: ({ userId, values }: { userId: string; values: { billingRate: number; costRate: number } }) =>
      setUserRate(userId, values),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: timeKeys.rates() }),
  });

  const members = membersQuery.data ?? [];
  const ratesByUser = new Map((ratesQuery.data ?? []).map((rate) => [rate.userId, rate]));

  return (
    <section
      className="rounded-[var(--radius)] border border-border bg-card shadow-sm"
      aria-labelledby="billing-rates-title"
    >
      <div className="border-b border-border p-4">
        <h2 id="billing-rates-title" className="text-sm font-semibold">
          Billing rates
        </h2>
        <p className="text-xs text-muted-foreground">
          Workspace-default hourly billing and cost rates applied to new time entries.
        </p>
      </div>

      {ratesQuery.isError ? (
        <p role="alert" className="m-4 rounded-lg border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300">
          Rates could not be loaded: {(ratesQuery.error as Error).message}
        </p>
      ) : null}
      {saveRate.isError ? (
        <p role="alert" className="m-4 rounded-lg border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300">
          This rate could not be saved: {(saveRate.error as Error).message}
        </p>
      ) : null}

      {ratesQuery.isLoading || membersQuery.isLoading ? (
        <p className="p-4 text-sm text-muted-foreground">Loading billing rates…</p>
      ) : members.length === 0 ? (
        <p className="p-4 text-sm text-muted-foreground">No workspace members to rate yet.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th scope="col" className="px-4 py-3 font-semibold">Member</th>
                <th scope="col" className="px-4 py-3 font-semibold">Billing rate</th>
                <th scope="col" className="px-4 py-3 font-semibold">Cost rate</th>
                <th scope="col" className="px-4 py-3 text-right font-semibold">Actions</th>
              </tr>
            </thead>
            <tbody>
              {members.map((member) => (
                <RateRow
                  // Remount on a rate change so the inputs pick up the persisted values.
                  key={`${member.userId}:${ratesByUser.get(member.userId)?.billingRate ?? "-"}:${ratesByUser.get(member.userId)?.costRate ?? "-"}`}
                  member={member}
                  rate={ratesByUser.get(member.userId)}
                  isSaving={saveRate.isPending}
                  onSave={(userId, values) => saveRate.mutate({ userId, values })}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

export default function TimePolicyPage() {
  const queryClient = useQueryClient();
  const policyQuery = useQuery({ queryKey: timeKeys.policy(), queryFn: getPolicy });
  const [localPolicy, setLocalPolicy] = useState<TimePolicy | null>(null);
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const policy = localPolicy ?? policyQuery.data;
  const savePolicy = useMutation({
    mutationFn: updatePolicy,
    onSuccess: () => {
      setLocalPolicy(null);
      setSavedAt(new Intl.DateTimeFormat("en", { hour: "numeric", minute: "2-digit" }).format(new Date()));
      void queryClient.invalidateQueries({ queryKey: timeKeys.policy() });
    },
  });

  function setBoolean(key: BooleanPolicyKey, checked: boolean) {
    setLocalPolicy((current) => {
      const basePolicy = current ?? policyQuery.data;
      return basePolicy ? { ...basePolicy, [key]: checked } : current;
    });
    setSavedAt(null);
  }

  function setNumber(key: NumericPolicyKey, value: number) {
    const nextValue = key === "weekStartsOn" ? Math.min(6, Math.max(0, value)) : value;
    setLocalPolicy((current) => {
      const basePolicy = current ?? policyQuery.data;
      return basePolicy ? { ...basePolicy, [key]: nextValue } : current;
    });
    setSavedAt(null);
  }

  function setReminderEnabled(checked: boolean) {
    setLocalPolicy((current) => {
      const basePolicy = current ?? policyQuery.data;
      return basePolicy ? { ...basePolicy, missingTimeReminderEnabled: checked } : current;
    });
    setSavedAt(null);
  }

  function setReminderCadence(value: TimePolicy["missingTimeReminderCadence"]) {
    setLocalPolicy((current) => {
      const basePolicy = current ?? policyQuery.data;
      return basePolicy ? { ...basePolicy, missingTimeReminderCadence: value } : current;
    });
    setSavedAt(null);
  }

  function setReminderMinimumHours(hours: number) {
    setLocalPolicy((current) => {
      const basePolicy = current ?? policyQuery.data;
      return basePolicy ? { ...basePolicy, missingTimeReminderMinimumSeconds: Math.max(0, hours) * 3600 } : current;
    });
    setSavedAt(null);
  }

  function setLockDate(value: string) {
    setLocalPolicy((current) => {
      const basePolicy = current ?? policyQuery.data;
      return basePolicy
        ? {
            ...basePolicy,
            lockDateUtc: value ? fromLocalDateTimeInputValue(value) : null,
          }
        : current;
    });
    setSavedAt(null);
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!policy) {
      return;
    }

    savePolicy.mutate(policy);
  }

  return (
    <section aria-labelledby="time-policy-title" className="space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">Administration</p>
        <h1 id="time-policy-title" className="mt-2 text-3xl font-semibold tracking-tight">
          Time policy
        </h1>
        <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
          Workspace-wide rules for timers, manual entries, and timesheet approval.
        </p>
      </div>

      <div className="rounded-[var(--radius)] border border-blue-200 bg-blue-50 p-4 text-sm text-blue-800 dark:border-blue-900 dark:bg-blue-950 dark:text-blue-200">
        Saving requires workspace administrator access. Everyone can read the policy in force.
      </div>

      {policyQuery.isError ? (
        <p role="alert" className={errorClassName}>
          The time policy could not be loaded: {(policyQuery.error as Error).message}
        </p>
      ) : null}

      {policyQuery.isLoading || !policy ? (
        <section className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
          Loading time policy…
        </section>
      ) : (
        <form className="space-y-6" onSubmit={handleSubmit}>
          {savePolicy.isError ? (
            <p role="alert" className={errorClassName}>
              This policy could not be saved: {(savePolicy.error as Error).message}
            </p>
          ) : null}

          <section className="rounded-[var(--radius)] border border-border bg-card shadow-sm" aria-labelledby="policy-toggles-title">
            <div className="border-b border-border p-4">
              <h2 id="policy-toggles-title" className="text-sm font-semibold">Rules</h2>
              <p className="text-xs text-muted-foreground">Toggle how timers and submissions behave.</p>
            </div>
            <div className="grid gap-3 p-4 md:grid-cols-2">
              {booleanFields.map((field) => (
                <label
                  key={field.key}
                  className="flex items-start gap-3 rounded-xl border border-border bg-background p-3"
                >
                  <input
                    type="checkbox"
                    checked={policy[field.key]}
                    className="mt-1 size-4 accent-[var(--primary)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                    onChange={(event) => setBoolean(field.key, event.target.checked)}
                  />
                  <span>
                    <span className="block text-sm font-medium">{field.label}</span>
                    <span className="mt-1 block text-xs leading-5 text-muted-foreground">{field.help}</span>
                  </span>
                </label>
              ))}
            </div>
          </section>

          <section className="rounded-[var(--radius)] border border-border bg-card shadow-sm" aria-labelledby="policy-numbers-title">
            <div className="border-b border-border p-4">
              <h2 id="policy-numbers-title" className="text-sm font-semibold">Limits and rounding</h2>
              <p className="text-xs text-muted-foreground">Numeric limits enforced when entries are created or edited.</p>
            </div>
            <div className="grid gap-4 p-4 md:grid-cols-2 xl:grid-cols-3">
              {numericFields.map((field) => (
                <label key={field.key} className="grid gap-2 text-sm font-medium">
                  {field.label}
                  <div className="flex overflow-hidden rounded-lg border border-border bg-background focus-within:outline focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-ring">
                    <input
                      type="number"
                      min={field.min}
                      max={field.key === "weekStartsOn" ? 6 : undefined}
                      step={field.step}
                      value={policy[field.key]}
                      className={numberInputClassName}
                      onChange={(event) => setNumber(field.key, Number(event.target.value))}
                    />
                    <span className="border-l border-border bg-muted px-3 py-2 text-xs text-muted-foreground">
                      {field.suffix}
                    </span>
                  </div>
                </label>
              ))}
              <label className="grid gap-2 text-sm font-medium">
                Lock date
                <input
                  type="datetime-local"
                  value={policy.lockDateUtc ? toLocalDateTimeInputValue(new Date(policy.lockDateUtc)) : ""}
                  className="rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                  onChange={(event) => setLockDate(event.target.value)}
                />
              </label>
            </div>
          </section>

          <section className="rounded-[var(--radius)] border border-border bg-card shadow-sm" aria-labelledby="policy-reminders-title">
            <div className="border-b border-border p-4">
              <h2 id="policy-reminders-title" className="text-sm font-semibold">Missing-time reminders</h2>
              <p className="text-xs text-muted-foreground">
                Notify members who haven&apos;t logged enough time by the end of the day or week.
              </p>
            </div>
            <div className="grid gap-4 p-4 md:grid-cols-3">
              <label className="flex items-start gap-3 rounded-xl border border-border bg-background p-3">
                <input
                  type="checkbox"
                  checked={policy.missingTimeReminderEnabled}
                  className="mt-1 size-4 accent-[var(--primary)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                  onChange={(event) => setReminderEnabled(event.target.checked)}
                />
                <span>
                  <span className="block text-sm font-medium">Enabled</span>
                  <span className="mt-1 block text-xs leading-5 text-muted-foreground">
                    Turns the reminder scheduler on for this workspace.
                  </span>
                </span>
              </label>
              <label className="grid gap-2 text-sm font-medium">
                Cadence
                <select
                  value={policy.missingTimeReminderCadence}
                  disabled={!policy.missingTimeReminderEnabled}
                  className="rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:opacity-50"
                  onChange={(event) => setReminderCadence(event.target.value as TimePolicy["missingTimeReminderCadence"])}
                >
                  <option value="Daily">By end of day</option>
                  <option value="Weekly">By end of week</option>
                </select>
              </label>
              <label className="grid gap-2 text-sm font-medium">
                Minimum tracked time
                <div className="flex overflow-hidden rounded-lg border border-border bg-background focus-within:outline focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-ring">
                  <input
                    type="number"
                    min={0}
                    step={0.5}
                    disabled={!policy.missingTimeReminderEnabled}
                    value={policy.missingTimeReminderMinimumSeconds / 3600}
                    className={`${numberInputClassName} disabled:opacity-50`}
                    onChange={(event) => setReminderMinimumHours(Number(event.target.value))}
                  />
                  <span className="border-l border-border bg-muted px-3 py-2 text-xs text-muted-foreground">
                    hours/period
                  </span>
                </div>
              </label>
            </div>
          </section>

          <div className="flex flex-wrap items-center justify-between gap-3 rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm">
            <p className="text-sm text-muted-foreground" role="status">
              {savePolicy.isPending
                ? "Saving policy…"
                : savedAt
                  ? `Policy saved at ${savedAt}.`
                  : localPolicy
                    ? "Unsaved changes."
                    : "Policy is up to date."}
            </p>
            <Button type="submit" disabled={savePolicy.isPending}>
              Save policy
            </Button>
          </div>
        </form>
      )}

      <BillingRatesCard />
    </section>
  );
}
