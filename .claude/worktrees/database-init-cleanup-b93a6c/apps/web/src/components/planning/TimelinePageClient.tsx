"use client";

import { useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { useAppContext } from "@/lib/app-context/AppContext";
import { getGantt } from "@/lib/planning/client";
import { planningKeys } from "@/lib/planning/queries";
import type { GanttBar } from "@/lib/planning/types";
import { useMemberDirectory } from "@/lib/members";
import { listSpaces } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";
import { cn } from "@/lib/utils";
import { formatShortDate } from "./helpers";
import { diffDays, getBarDates, getTimelineRange, HeaderTicks, type Zoom } from "./timelinePrimitives";

const UNASSIGNED = "unassigned";

function groupByAssignee(bars: GanttBar[]) {
  const groups = new Map<string, GanttBar[]>();
  for (const bar of bars) {
    const key = bar.assigneeUserIds[0] ?? UNASSIGNED;
    groups.set(key, [...(groups.get(key) ?? []), bar]);
  }
  return groups;
}

/**
 * Timeline -- distinct from Gantt (per spec, they're separate SavedViewType values). Reuses the
 * same /views/gantt data and date-range bar-math (timelinePrimitives.ts, extracted from
 * GanttPageClient.tsx) but presents it as a plain swimlane-per-assignee chart: no dependency arrows, no
 * critical-path highlighting, no baseline overlay, no drag-to-reschedule. Just "who has what, when".
 */
export function TimelinePageClient() {
  const { workspaceId = "" } = useAppContext();
  const [zoom, setZoom] = useState<Zoom>("day");
  const [override, setOverride] = useState("");
  const directory = useMemberDirectory();

  const spacesQuery = useQuery({ queryKey: workKeys.spaces(), queryFn: listSpaces });
  const spaces = useMemo(() => spacesQuery.data ?? [], [spacesQuery.data]);

  const storageKey = workspaceId ? `planvexa-timeline-space:${workspaceId}` : null;
  const selectedSpaceId = useMemo(() => {
    if (override && spaces.some((space) => space.id === override)) return override;
    const stored = storageKey && typeof window !== "undefined" ? window.localStorage.getItem(storageKey) : null;
    if (stored && spaces.some((space) => space.id === stored)) return stored;
    return spaces[0]?.id ?? "";
  }, [override, spaces, storageKey]);

  function selectSpace(spaceId: string) {
    setOverride(spaceId);
    if (storageKey) window.localStorage.setItem(storageKey, spaceId);
  }

  const params = useMemo(() => ({ spaceId: selectedSpaceId }), [selectedSpaceId]);
  const ganttQuery = useQuery({
    queryKey: planningKeys.gantt(params),
    queryFn: () => getGantt(params),
    enabled: Boolean(selectedSpaceId),
  });
  const bars = useMemo(() => ganttQuery.data ?? [], [ganttQuery.data]);
  const selectedSpace = spaces.find((space) => space.id === selectedSpaceId);
  const range = useMemo(() => getTimelineRange(bars), [bars]);
  const rangeDays = Math.max(1, diffDays(range.start, range.end) + 1);
  const pxPerDay = zoom === "day" ? 42 : 14;
  const timelineWidth = rangeDays * pxPerDay;
  const lanes = useMemo(() => groupByAssignee(bars), [bars]);

  return (
    <section aria-labelledby="timeline-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Planning · Visualization</p>
          <h1 id="timeline-title" className="mt-2 text-3xl font-semibold tracking-tight">
            Timeline
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            A simpler swimlane-per-assignee view of date-ranged tasks — no dependency arrows or
            critical path. For those, see{" "}
            <a href="/app/gantt" className="text-primary underline-offset-2 hover:underline">
              Gantt
            </a>
            .
          </p>
        </div>
        <fieldset className="flex items-center gap-2">
          <legend className="sr-only">Timeline zoom</legend>
          {(["day", "week"] as const).map((option) => (
            <Button
              key={option}
              type="button"
              variant={zoom === option ? "primary" : "outline"}
              size="sm"
              aria-pressed={zoom === option}
              onClick={() => setZoom(option)}
            >
              {option === "day" ? "Day" : "Week"}
            </Button>
          ))}
        </fieldset>
      </div>

      <div className="flex items-center gap-2">
        <label htmlFor="timeline-space" className="text-sm font-medium">
          Space
        </label>
        <select
          id="timeline-space"
          value={selectedSpaceId}
          disabled={spaces.length === 0}
          onChange={(event) => selectSpace(event.target.value)}
          className="h-9 min-w-[14rem] rounded-lg border border-border bg-background px-2 text-sm shadow-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        >
          {spaces.length === 0 ? <option value="">No spaces</option> : null}
          {spaces.map((space) => (
            <option key={space.id} value={space.id}>
              {space.name}
            </option>
          ))}
        </select>
      </div>

      <section className="rounded-[var(--radius)] border border-border bg-card shadow-sm">
        <header className="border-b border-border p-4">
          <h2 className="text-sm font-semibold">{selectedSpace ? `${selectedSpace.name} timeline` : "Timeline"}</h2>
          <p className="mt-1 text-xs text-muted-foreground">
            {formatShortDate(range.start)} – {formatShortDate(range.end)} · one swimlane per assignee.
          </p>
        </header>
        <div className="overflow-x-auto" aria-label="Horizontally scrollable timeline">
          <div className="min-w-[760px]">
            <div className="grid" style={{ gridTemplateColumns: `17rem ${timelineWidth}px` }}>
              <div className="sticky left-0 z-20 border-b border-r border-border bg-card px-4 py-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                Assignee
              </div>
              <div style={{ width: timelineWidth }}>
                <HeaderTicks start={range.start} rangeDays={rangeDays} pxPerDay={pxPerDay} zoom={zoom} />
              </div>

              {ganttQuery.isLoading ? (
                <div className="col-span-2 p-4 text-sm text-muted-foreground">Loading timeline…</div>
              ) : spaces.length === 0 ? (
                <div className="col-span-2 p-4">
                  <EmptyState title="No spaces yet" description="Create a Space and add tasks with dates to see them here." />
                </div>
              ) : bars.length === 0 ? (
                <div className="col-span-2 p-4">
                  <EmptyState
                    title="Nothing to schedule yet"
                    description="Bars appear here once tasks have a start or due date."
                  />
                </div>
              ) : (
                [...lanes.entries()].map(([assigneeUserId, laneBars]) => (
                  <div key={assigneeUserId} className="contents">
                    <div className="sticky left-0 z-10 border-b border-r border-border bg-card p-4">
                      <h3 className="text-sm font-semibold">
                        {assigneeUserId === UNASSIGNED ? "Unassigned" : directory.getLabel(assigneeUserId)}
                      </h3>
                      <p className="mt-1 text-xs text-muted-foreground">{laneBars.length} tasks</p>
                    </div>
                    <div className="relative border-b border-border bg-background" style={{ width: timelineWidth, minHeight: laneBars.length * 32 + 16 }}>
                      {laneBars.map((bar, index) => {
                        const { start, end } = getBarDates(bar);
                        const left = Math.max(0, diffDays(range.start, start) * pxPerDay);
                        const width = Math.max(pxPerDay * 0.9, (diffDays(start, end) + 1) * pxPerDay);

                        return (
                          <div
                            key={bar.id}
                            className={cn(
                              "absolute h-6 truncate rounded-full border border-primary/40 bg-primary/15 px-2 text-[0.7rem] leading-6 text-foreground shadow-sm",
                              bar.progress >= 1 && "opacity-60 line-through",
                            )}
                            style={{ left, width, top: 8 + index * 32 }}
                            title={bar.title}
                          >
                            {bar.title}
                          </div>
                        );
                      })}
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>
        </div>
      </section>
    </section>
  );
}
