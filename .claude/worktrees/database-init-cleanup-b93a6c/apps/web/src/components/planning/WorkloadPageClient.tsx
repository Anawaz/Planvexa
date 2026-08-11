"use client";

import { useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { getWorkload } from "@/lib/planning/client";
import { planningKeys } from "@/lib/planning/queries";
import type { WorkloadRow } from "@/lib/planning/types";
import { cn } from "@/lib/utils";
import { addDays, formatHours, formatLongDate, startOfUtcWeek } from "./helpers";

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
  const rows = workloadQuery.data ?? [];
  const maxHours = Math.max(
    1,
    ...rows.flatMap((row) => [row.capacityHours, row.scheduledHours, row.loggedHours]),
  );
  const overAllocatedCount = rows.filter((row) => row.isOverAllocated).length;

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
            Data source
          </p>
          <p className="mt-2 text-lg font-semibold">/api/v1/views/workload</p>
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

              return (
                <article
                  key={row.userId}
                  className={cn(
                    "grid gap-4 p-4 lg:grid-cols-[18rem_1fr]",
                    row.isOverAllocated && "bg-amber-50 dark:bg-amber-950/20",
                  )}
                >
                  <div className="flex items-center gap-3">
                    {/* ponytail: raw user ids until Chunk B adds the member directory. */}
                    <span className="grid size-11 place-items-center rounded-full border border-border bg-background text-sm font-semibold">
                      {row.userId.slice(0, 2).toUpperCase()}
                    </span>
                    <div>
                      <h2 className="text-sm font-semibold">{row.userId}</h2>
                      <p className="text-xs text-muted-foreground">Teammate</p>
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
                  </div>
                  <UtilizationBar row={row} maxHours={maxHours} />
                </article>
              );
            })}
          </div>
        )}
      </section>
    </section>
  );
}
