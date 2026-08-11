"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { FormEvent } from "react";
import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { getRetentionPolicy, updateRetentionPolicy } from "@/lib/ai/client";
import { aiKeys } from "@/lib/ai/queries";
import type { RetentionPolicy } from "@/lib/ai/types";
import { cn } from "@/lib/utils";

const panelClassName = "rounded-[var(--radius)] border border-border bg-card shadow-sm";
const numberInputClassName =
  "h-10 rounded-lg border border-border bg-background px-3 text-sm outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-not-allowed disabled:opacity-50";

const defaultPolicy: RetentionPolicy = {
  deletedTaskRetentionDays: 90,
  auditRetentionDays: 730,
  legalHold: false,
};

function normalizeDays(value: number) {
  return Number.isFinite(value) ? Math.max(0, Math.floor(value)) : 0;
}

export default function RetentionPage() {
  const queryClient = useQueryClient();
  const [localDraft, setLocalDraft] = useState<RetentionPolicy | null>(null);
  const [statusMessage, setStatusMessage] = useState("");

  const policyQuery = useQuery({ queryKey: aiKeys.retention(), queryFn: getRetentionPolicy });
  const updateMutation = useMutation({
    mutationFn: updateRetentionPolicy,
    onSuccess: (policy) => {
      setLocalDraft(policy);
      setStatusMessage("Retention policy saved.");
      void queryClient.invalidateQueries({ queryKey: aiKeys.retention() });
    },
  });

  function setDays(field: "deletedTaskRetentionDays" | "auditRetentionDays", value: number) {
    setLocalDraft((current) => ({ ...(current ?? policyQuery.data ?? defaultPolicy), [field]: normalizeDays(value) }));
    setStatusMessage("");
  }

  function submitPolicy(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    updateMutation.mutate(draft);
  }

  const draft = localDraft ?? policyQuery.data ?? defaultPolicy;

  return (
    <section aria-labelledby="retention-title" className="space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">Data retention</p>
        <h1 id="retention-title" className="mt-2 text-3xl font-semibold tracking-tight">
          Data Retention
        </h1>
        <p className="mt-3 max-w-3xl text-sm leading-6 text-muted-foreground">
          Configure workspace retention defaults; purge jobs read this policy.
        </p>
      </div>

      {statusMessage ? (
        <p role="status" className="rounded-lg bg-primary/10 px-4 py-3 text-sm font-medium text-primary">
          {statusMessage}
        </p>
      ) : null}

      {policyQuery.isLoading ? (
        <section className={cn(panelClassName, "p-6 text-sm text-muted-foreground")}>Loading retention policy…</section>
      ) : (
        <form onSubmit={submitPolicy} className={cn(panelClassName, "space-y-6 p-5")}>
          <fieldset className="grid gap-5 lg:grid-cols-2">
            <legend className="sr-only">Retention windows</legend>
            <label htmlFor="deleted-task-retention" className="grid gap-2 text-sm font-medium">
              Deleted task retention
              <input
                id="deleted-task-retention"
                type="number"
                min={0}
                step={1}
                value={draft.deletedTaskRetentionDays}
                onChange={(event) => setDays("deletedTaskRetentionDays", Number(event.target.value))}
                className={numberInputClassName}
                aria-describedby="deleted-task-retention-help"
              />
              <span id="deleted-task-retention-help" className="text-xs leading-5 text-muted-foreground">
                Days to keep deleted task records. Set to 0 to keep deleted tasks forever.
              </span>
            </label>

            <label htmlFor="audit-retention" className="grid gap-2 text-sm font-medium">
              Audit retention
              <input
                id="audit-retention"
                type="number"
                min={0}
                step={1}
                value={draft.auditRetentionDays}
                onChange={(event) => setDays("auditRetentionDays", Number(event.target.value))}
                className={numberInputClassName}
                aria-describedby="audit-retention-help"
              />
              <span id="audit-retention-help" className="text-xs leading-5 text-muted-foreground">
                Days to keep audit events for search and export. Set to 0 only when policy requires indefinite retention.
              </span>
            </label>
          </fieldset>

          <fieldset className="space-y-3">
            <legend className="text-lg font-semibold">Legal hold</legend>
            <label
              className={cn(
                "flex items-start gap-3 rounded-xl border p-4 focus-within:outline focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-ring",
                draft.legalHold
                  ? "border-amber-300 bg-amber-50 dark:border-amber-800 dark:bg-amber-950"
                  : "border-border bg-background",
              )}
            >
              <input
                type="checkbox"
                checked={draft.legalHold}
                onChange={(event) => {
                  setLocalDraft((current) => ({
                    ...(current ?? policyQuery.data ?? defaultPolicy),
                    legalHold: event.target.checked,
                  }));
                  setStatusMessage("");
                }}
                className="mt-1 size-4 rounded border-border accent-[var(--primary)]"
              />
              <span>
                <span className="block text-sm font-semibold">Enable legal hold</span>
                <span className="mt-1 block text-xs leading-5 text-muted-foreground">
                  Preserve all workspace records while legal, compliance, or investigation obligations are active.
                </span>
              </span>
            </label>

            {draft.legalHold ? (
              <p
                role="alert"
                className="rounded-xl border border-amber-300 bg-amber-50 p-4 text-sm font-semibold text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100"
              >
                Legal hold blocks all automated purging, including deleted tasks and audit events, until it is turned off.
              </p>
            ) : (
              <p className="rounded-xl border border-border bg-background p-4 text-sm text-muted-foreground">
                Automated purging will follow the retention windows after backend jobs are connected.
              </p>
            )}
          </fieldset>

          <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-5">
            <p className="text-sm text-muted-foreground">Applies to every workspace in this account.</p>
            <Button type="submit" disabled={updateMutation.isPending}>
              Save retention policy
            </Button>
          </div>
        </form>
      )}
    </section>
  );
}
