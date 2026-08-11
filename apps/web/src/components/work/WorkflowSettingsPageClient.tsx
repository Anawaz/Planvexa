"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { listStatusSchemes, setStatusTransitions } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";
import type { StatusDefinition, StatusScheme } from "@/lib/work/types";
import { cn } from "@/lib/utils";

/** One status's outgoing-transition restriction: unrestricted by default, or a checked subset of the
 * scheme's other statuses once an admin opts in. Enforced server-side (WorkItemService), not only here. */
function StatusTransitionRow({ scheme, status }: { scheme: StatusScheme; status: StatusDefinition }) {
  const queryClient = useQueryClient();
  const others = scheme.statuses.filter((s) => s.id !== status.id);
  const restricted = status.allowedNextStatusIds.length > 0;

  const mutation = useMutation({
    mutationFn: (toStatusIds: string[]) => setStatusTransitions(scheme.id, status.id, toStatusIds),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: workKeys.statusSchemes() }),
  });

  function toggleRestricted(checked: boolean) {
    // Turning restriction ON starts from "every other status allowed" (no behavior change yet) so an
    // admin never accidentally creates a dead-end status with zero allowed transitions.
    mutation.mutate(checked ? others.map((o) => o.id) : []);
  }

  function toggleTarget(targetId: string, checked: boolean) {
    const next = checked
      ? [...status.allowedNextStatusIds, targetId]
      : status.allowedNextStatusIds.filter((id) => id !== targetId);
    mutation.mutate(next);
  }

  return (
    <div className="grid gap-2 border-b border-border p-3 last:border-b-0">
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <span className="inline-block size-2.5 rounded-full" style={{ backgroundColor: status.color }} />
          <p className="text-sm font-semibold">{status.name}</p>
        </div>
        <label className="flex items-center gap-2 text-xs font-medium text-muted-foreground">
          <input
            type="checkbox"
            checked={restricted}
            disabled={mutation.isPending || others.length === 0}
            onChange={(event) => toggleRestricted(event.target.checked)}
            className="size-4 rounded border-border accent-[var(--primary)]"
          />
          Restrict where this can go
        </label>
      </div>

      {restricted ? (
        <fieldset className="ml-5 flex flex-wrap gap-x-4 gap-y-1">
          <legend className="sr-only">Statuses {status.name} may move to</legend>
          {others.map((target) => (
            <label key={target.id} className={cn("flex items-center gap-1.5 text-xs", mutation.isPending && "opacity-60")}>
              <input
                type="checkbox"
                checked={status.allowedNextStatusIds.includes(target.id)}
                disabled={mutation.isPending}
                onChange={(event) => toggleTarget(target.id, event.target.checked)}
                className="size-3.5 rounded border-border accent-[var(--primary)]"
              />
              {target.name}
            </label>
          ))}
        </fieldset>
      ) : (
        <p className="ml-5 text-xs text-muted-foreground">Can move to any status in this workflow.</p>
      )}
    </div>
  );
}

export function WorkflowSettingsPageClient() {
  const schemesQuery = useQuery({ queryKey: workKeys.statusSchemes(), queryFn: listStatusSchemes });
  const schemes = schemesQuery.data ?? [];

  return (
    <section aria-labelledby="workflows-settings-title" className="space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">Settings</p>
        <h1 id="workflows-settings-title" className="mt-2 text-3xl font-semibold tracking-tight">
          Workflows
        </h1>
        <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
          For each status in a workflow, optionally restrict which statuses it can move to next. A
          status with no restriction can move to any other status.
        </p>
      </div>

      {schemesQuery.isLoading ? (
        <p className="text-sm text-muted-foreground">Loading workflows…</p>
      ) : schemes.length === 0 ? (
        <p className="rounded-lg border border-dashed border-border p-3 text-sm text-muted-foreground">
          No status schemes yet.
        </p>
      ) : (
        schemes.map((scheme) => (
          <section
            key={scheme.id}
            aria-labelledby={`scheme-${scheme.id}-title`}
            className="rounded-[var(--radius)] border border-border bg-card shadow-sm"
          >
            <header className="border-b border-border p-4">
              <h2 id={`scheme-${scheme.id}-title`} className="text-sm font-semibold">
                {scheme.name}
              </h2>
            </header>
            <div>
              {[...scheme.statuses]
                .sort((a, b) => a.position - b.position)
                .map((status) => (
                  <StatusTransitionRow key={status.id} scheme={scheme} status={status} />
                ))}
            </div>
          </section>
        ))
      )}
    </section>
  );
}
