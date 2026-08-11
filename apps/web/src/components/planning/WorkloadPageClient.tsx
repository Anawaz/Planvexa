"use client";

import { useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { Avatar } from "@/components/ui/Avatar";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { useMemberDirectory } from "@/lib/members";
import { getAssigneeDrillDown, getWorkload } from "@/lib/planning/client";
import { planningKeys } from "@/lib/planning/queries";
import type { WorkloadRow } from "@/lib/planning/types";
import { cn } from "@/lib/utils";
import { addDays, formatHours, formatLongDate, startOfUtcWeek } from "./helpers";

// Sentinel for the Workload view's "Unassigned" bucket (open tasks with no assignee) — mirrors the
// backend's WorkloadService, which uses Guid.Empty for the same row rather than a nullable UserId.
const UNASSIGNED_USER_ID = "00000000-0000-0000-0000-000000000000";

function UtilizationBar({
  row,
  maxHours,
}: {
  row: WorkloadRow;
  maxHours: number;
}) {
  const scheduledWidth = Math.min(100, (row.scheduledHours / maxHours) * 100);
  const loggedWidth = Math.min(100, (row.loggedHours / maxHours) * 100);
  const capacityWidth = Math.min(100, (row.capacityHours / maxHours) * 100);

  return (
    <div className="space-y-2">
      <div className="h-3 rounded-full bg-muted" aria-hidden="true">
        <div
          className={cn(
            "h-3 rounded-full",
            row.isOverAllocated ? "bg-amber-500" : "bg-primary",
          )}
          style={{ width: `${scheduledWidth}%` }}
        />
      </div>
      <div className="grid gap-2 text-xs text-muted-foreground sm:grid-cols-3">
        <span>
          Capacity{" "}
          <strong className="font-semibold text-foreground">{formatHours(row.capacityHours)}</strong>
        </span>
        <span>
          Scheduled{" "}
          <strong className="font-semibold text-foreground">
            {formatHours(row.scheduledHours)}
          </strong>
        </span>
        <span>
          Logged{" "}
          <strong className="font-semibold text-foreground">{formatHours(row.loggedHours)}</strong>
        </span>
      </div>
      <div className="relative h-2 rounded-full bg-muted" aria-hidden="true">
        <div className="absolute h-2 rounded-full bg-blue-400" style={{ width: `${loggedWidth}%` }} />
        <div
          className="absolute top-[-0.25rem] h-4 w-0.5 rounded-full bg-foreground/70"
          style={{ left: `${capacityWidth}%` }}
        />
      </div>
    </div>
  );
}

export function WorkloadPageClient() {
  const [rangeStart, setRangeStart] = useState(() => startOfUtcWeek(new Date()));
  const rangeEnd = useMemo(() => addDays(rangeStart, 13), [rangeStart]);
  const params = useMemo(
    () => ({ from: rangeStart.toISOString(), to: rangeEnd.toISOString() }),
    [rangeEnd, rangeStart],
  );
  const workloadQuery = useQuery({
    queryKey: planningKeys.workload(params),
    queryFn: () => getWorkload(params),
  });
  const { getLabel, getInitials, getAvatarUrl } = useMemberDirectory();
  const rows = workloadQuery.data ?? [];
  const maxHours = Math.max(
    1,
    ...rows.flatMap((row) => [row.capacityHours, row.scheduledHours, row.loggedHours]),
  );
  const overAllocatedCount = rows.filter((row) => row.isOverAllocated).length;
  const teammateCount = rows.filter((row) => row.userId !== UNASSIGNED_USER_ID).length;

  const [drillDownUserId, setDrillDownUserId] = useState<string | null>(null);
  const drillDownQuery = useQuery({
    queryKey: ["reporting", "drill-down", "assignee", drillDownUserId],
    queryFn: () => getAssigneeDrillDown(drillDownUserId!),
    enabled: drillDownUserId !== null,
  });

  return (
    <section aria-labelledby="workload-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Capacity</p>
          <h1 id="workload-title" className="mt-2 text-3xl font-semibold tracking-tight">
            Workload
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            Capacity, scheduled effort, and logged hours by teammate for a server-filtered
            planning range.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2" aria-label="Workload date range">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => setRangeStart((current) => addDays(current, -14))}
          >
            Previous range
          </Button>
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={() => setRangeStart(startOfUtcWeek(new Date()))}
          >
            Current range
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => setRangeStart((current) => addDays(current, 14))}
          >
            Next range
          </Button>
        </div>
      </div>

      <div className="grid gap-4 md:grid-cols-3">
        <article className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Range</p>
          <p className="mt-2 text-lg font-semibold">
            {formatLongDate(rangeStart)} – {formatLongDate(rangeEnd)}
          </p>
        </article>
        <article className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
            Over-allocated
          </p>
          <p className="mt-2 text-lg font-semibold">{overAllocatedCount} teammates</p>
        </article>
        <article className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
            Teammates shown
          </p>
          <p className="mt-2 text-lg font-semibold">{teammateCount}</p>
        </article>
      </div>

      <section
        className="rounded-[var(--radius)] border border-border bg-card shadow-sm"
        aria-label="Workload rows"
      >
        {workloadQuery.isLoading ? (
          <p className="p-4 text-sm text-muted-foreground">Loading workload…</p>
        ) : rows.length === 0 ? (
          <EmptyState
            className="m-4"
            title="No scheduled work yet"
            description="A teammate appears here once they are assigned a task with an estimate or a due date."
          />
        ) : (
          <div className="divide-y divide-border">
            {rows.map((row) => {
              const utilization = row.capacityHours
                ? Math.round((row.scheduledHours / row.capacityHours) * 100)
                : 0;
              const isUnassigned = row.userId === UNASSIGNED_USER_ID;

              return (
                <article
                  key={row.userId}
                  className={cn(
                    "grid gap-4 p-4 lg:grid-cols-[18rem_1fr]",
                    row.isOverAllocated && "bg-amber-50 dark:bg-amber-950/20",
                  )}
                >
                  <button
                    type="button"
                    className="flex items-center gap-3 rounded-lg text-left focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                    onClick={() => setDrillDownUserId(row.userId)}
                    aria-label={`View tasks for ${isUnassigned ? "Unassigned" : getLabel(row.userId)}`}
                  >
                    {isUnassigned ? (
                      <span
                        className="grid size-11 shrink-0 place-items-center rounded-full border border-dashed border-border bg-background text-sm font-semibold text-muted-foreground"
                        aria-hidden="true"
                      >
                        ?
                      </span>
                    ) : (
                      <Avatar
                        avatarUrl={getAvatarUrl(row.userId)}
                        initials={getInitials(row.userId)}
                        className="grid size-11 place-items-center rounded-full border border-border bg-background text-sm font-semibold"
                      />
                    )}
                    <div>
                      <h2 className="text-sm font-semibold underline decoration-dotted underline-offset-2">
                        {isUnassigned ? "Unassigned" : getLabel(row.userId)}
                      </h2>
                      <p className="text-xs text-muted-foreground">
                        {isUnassigned ? "Nobody owns these tasks" : "Teammate"}
                      </p>
                    </div>
                    <span
                      className={cn(
                        "ml-auto rounded-full px-2.5 py-1 text-xs font-semibold",
                        row.isOverAllocated
                          ? "bg-amber-100 text-amber-700 dark:bg-amber-900 dark:text-amber-200"
                          : "bg-emerald-100 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300",
                      )}
                    >
                      {utilization}%
                    </span>
                  </button>
                  <UtilizationBar row={row} maxHours={maxHours} />
                </article>
              );
            })}
          </div>
        )}
      </section>

      {drillDownUserId !== null ? (
        <section
          className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm"
          aria-labelledby="workload-drill-down-title"
        >
          <div className="flex items-center justify-between gap-3">
            <h2 id="workload-drill-down-title" className="text-sm font-semibold">
              Tasks for{" "}
              {drillDownUserId === UNASSIGNED_USER_ID ? "Unassigned" : getLabel(drillDownUserId)}
            </h2>
            <Button type="button" size="sm" variant="ghost" onClick={() => setDrillDownUserId(null)}>
              Close
            </Button>
          </div>
          <ul className="mt-3 divide-y divide-border text-sm">
            {drillDownQuery.isLoading ? (
              <li className="py-2 text-muted-foreground">Loading tasks…</li>
            ) : (drillDownQuery.data ?? []).length === 0 ? (
              <li className="py-2 text-muted-foreground">No visible tasks.</li>
            ) : (
              drillDownQuery.data!.map((task) => (
                <li key={task.taskId} className="flex items-center justify-between gap-3 py-2">
                  <span>{task.title}</span>
                  <span className="text-xs text-muted-foreground">{task.statusName}</span>
                </li>
              ))
            )}
          </ul>
        </section>
      ) : null}
    </section>
  );
}
