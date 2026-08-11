"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useRef, useState } from "react";
import type { CSSProperties, KeyboardEvent as ReactKeyboardEvent, PointerEvent as ReactPointerEvent } from "react";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { useAppContext } from "@/lib/app-context/AppContext";
import { getGantt, listHolidays, getWorkSchedule } from "@/lib/planning/client";
import { planningKeys } from "@/lib/planning/queries";
import type { GanttBar } from "@/lib/planning/types";
import { listSpaces, setTaskBaseline, updateTask } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";
import { cn } from "@/lib/utils";
import { addDays, dayKey, formatShortDate, shiftDateIso } from "./helpers";
import {
  diffDays,
  getBarDates,
  getTimelineRange,
  HeaderTicks,
  normalizeDate,
  type Zoom,
} from "./timelinePrimitives";

/**  . uc(s)hades weekend/holiday day columns behind the bars so duration reads
 * against the actual working calendar, not just the raw date axis. */
function NonWorkingDaysOverlay({
  rangeStart,
  rangeDays,
  pxPerDay,
  workingDaysMask,
  holidayDateKeys,
}: {
  rangeStart: Date;
  rangeDays: number;
  pxPerDay: number;
  workingDaysMask: number[];
  holidayDateKeys: Set<string>;
}) {
  const days = Array.from({ length: rangeDays }, (_, index) => addDays(rangeStart, index));

  return (
    <div className="pointer-events-none absolute inset-0" aria-hidden="true">
      {days.map((day, index) => {
        // ISO day of week: Sunday=0 in JS Date -> 7 for the 1..7 (Mon..Sun) mask backend uses.
        const isoDay = day.getUTCDay() === 0 ? 7 : day.getUTCDay();
        const isNonWorking = !workingDaysMask.includes(isoDay) || holidayDateKeys.has(dayKey(day));
        if (!isNonWorking) {
          return null;
        }

        return (
          <div
            key={dayKey(day)}
            className="absolute inset-y-0 bg-muted/60"
            style={{ left: index * pxPerDay, width: pxPerDay }}
          />
        );
      })}
    </div>
  );
}

function TimelineBar({
  bar,
  rangeStart,
  pxPerDay,
  onReschedule,
  disabled,
}: {
  bar: GanttBar;
  rangeStart: Date;
  pxPerDay: number;
  onReschedule: (deltaDays: number) => void;
  disabled: boolean;
}) {
  const { start, end } = getBarDates(bar);
  const left = Math.max(0, diffDays(rangeStart, start) * pxPerDay);
  const width = Math.max(pxPerDay * 0.9, (diffDays(start, end) + 1) * pxPerDay);
  const progress = Math.min(100, Math.max(0, bar.progress));

  // A fainter secondary bar at the last-captured planned start/due date, so drift
  // from the plan reads at a glance. Null until "Set baseline" has been used at least once.
  const baseline =
    bar.baselineStartDate || bar.baselineDueDate
      ? (() => {
          const baselineStart = normalizeDate(bar.baselineStartDate ?? bar.baselineDueDate ?? start.toISOString());
          const baselineEnd = normalizeDate(bar.baselineDueDate ?? bar.baselineStartDate ?? end.toISOString());
          return {
            left: Math.max(0, diffDays(rangeStart, baselineStart) * pxPerDay),
            width: Math.max(pxPerDay * 0.9, (diffDays(baselineStart, baselineEnd) + 1) * pxPerDay),
          };
        })()
      : null;

  const [dragOffset, setDragOffset] = useState(0);
  const dragRef = useRef<{ startX: number } | null>(null);

  function handlePointerDown(event: ReactPointerEvent<HTMLDivElement>) {
    if (disabled) return;
    dragRef.current = { startX: event.clientX };
    event.currentTarget.setPointerCapture(event.pointerId);
  }

  function handlePointerMove(event: ReactPointerEvent<HTMLDivElement>) {
    if (!dragRef.current) return;
    setDragOffset(event.clientX - dragRef.current.startX);
  }

  function handlePointerUp(event: ReactPointerEvent<HTMLDivElement>) {
    if (!dragRef.current) return;
    const deltaDays = Math.round((event.clientX - dragRef.current.startX) / pxPerDay);
    dragRef.current = null;
    setDragOffset(0);
    if (deltaDays !== 0) {
      onReschedule(deltaDays);
    }
  }

  function handleKeyDown(event: ReactKeyboardEvent<HTMLDivElement>) {
    if (disabled) return;
    if (event.key === "ArrowLeft") {
      event.preventDefault();
      onReschedule(-1);
    } else if (event.key === "ArrowRight") {
      event.preventDefault();
      onReschedule(1);
    }
  }

  const commonProps = {
    role: "button" as const,
    tabIndex: disabled ? -1 : 0,
    onPointerDown: handlePointerDown,
    onPointerMove: handlePointerMove,
    onPointerUp: handlePointerUp,
    onKeyDown: handleKeyDown,
  };

  if (bar.isMilestone) {
    return (
      <>
        <div
          {...commonProps}
          className={cn(
            "absolute top-5 size-5 rotate-45 rounded-sm border shadow-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring",
            bar.isCritical ? "border-red-500 bg-red-500" : "border-primary bg-primary",
            disabled ? "cursor-default" : "cursor-grab active:cursor-grabbing",
          )}
          style={{ left, transform: `translateX(${dragOffset}px) rotate(45deg)` }}
          aria-label={`${bar.title} milestone due ${formatShortDate(end)}${bar.isCritical ? ", on the critical path" : ""}. Drag or use arrow keys to reschedule.`}
        />
      </>
    );
  }

  return (
    <>
      {baseline ? (
        <div
          aria-hidden="true"
          className="absolute top-2 h-1.5 rounded-full bg-foreground/20"
          style={{ left: baseline.left, width: baseline.width }}
          title="Baseline (planned) dates"
        />
      ) : null}
      <div
        {...commonProps}
        className={cn(
          "absolute top-4 h-7 overflow-hidden rounded-full border shadow-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring",
          bar.isCritical ? "border-red-500 bg-red-500/15" : "border-primary/40 bg-primary/15",
          disabled ? "cursor-default" : "cursor-grab active:cursor-grabbing",
        )}
        style={{ left, width, transform: `translateX(${dragOffset}px)` }}
        aria-label={`${bar.title}, ${progress}% complete${bar.isCritical ? ", on the critical path" : ""}. Drag or use arrow keys to reschedule.`}
      >
        <div
          className={cn("pointer-events-none h-full rounded-full", bar.isCritical ? "bg-red-500/70" : "bg-primary/70")}
          style={{ width: `${progress}%` }}
        />
      </div>
    </>
  );
}

export function GanttPageClient() {
  const { workspaceId = "" } = useAppContext();
  const [zoom, setZoom] = useState<Zoom>("day");
  const [override, setOverride] = useState<string>("");

  const spacesQuery = useQuery({
    queryKey: workKeys.spaces(),
    queryFn: listSpaces,
  });
  const spaces = useMemo(() => spacesQuery.data ?? [], [spacesQuery.data]);

  // The effective Space is the user's explicit choice, else the last-viewed Space remembered for
  // this Workspace, else the first Space. Derived (not effect state) to avoid stale sync issues.
  const storageKey = workspaceId ? `planvexa-gantt-space:${workspaceId}` : null;
  const selectedSpaceId = useMemo(() => {
    if (override && spaces.some((space) => space.id === override)) {
      return override;
    }
    const stored = storageKey && typeof window !== "undefined" ? window.localStorage.getItem(storageKey) : null;
    if (stored && spaces.some((space) => space.id === stored)) {
      return stored;
    }
    return spaces[0]?.id ?? "";
  }, [override, spaces, storageKey]);

  function selectSpace(spaceId: string) {
    setOverride(spaceId);
    if (storageKey) {
      window.localStorage.setItem(storageKey, spaceId);
    }
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
  const criticalCount = bars.filter((bar) => bar.isCritical).length;

  // Weekend/holiday shading behind the bars.
  const scheduleQuery = useQuery({ queryKey: planningKeys.workSchedule(), queryFn: getWorkSchedule });
  const holidaysQuery = useQuery({ queryKey: planningKeys.holidays(), queryFn: listHolidays });
  const workingDaysMask = scheduleQuery.data?.workingDays ?? [1, 2, 3, 4, 5];
  const holidayDateKeys = useMemo(
    () => new Set((holidaysQuery.data ?? []).map((holiday) => dayKey(new Date(holiday.dateUtc)))),
    [holidaysQuery.data],
  );

  const queryClient = useQueryClient();
  const reschedule = useMutation({
    mutationFn: ({ id, startDate, dueDate }: { id: string; startDate: string | null; dueDate: string | null }) =>
      updateTask(id, { startDate: startDate ?? undefined, dueDate: dueDate ?? undefined }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: planningKeys.gantt(params) });
      void queryClient.invalidateQueries({ queryKey: workKeys.all });
    },
  });

  const captureBaseline = useMutation({
    mutationFn: (taskId: string) => setTaskBaseline(taskId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: planningKeys.gantt(params) });
    },
  });

  function rescheduleBar(bar: GanttBar, deltaDays: number) {
    if (!bar.startDate && !bar.dueDate) {
      return;
    }
    reschedule.mutate({
      id: bar.id,
      startDate: shiftDateIso(bar.startDate, deltaDays),
      dueDate: shiftDateIso(bar.dueDate, deltaDays),
    });
  }

  return (
    <section aria-labelledby="gantt-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Planning · Visualization</p>
          <h1 id="gantt-title" className="mt-2 text-3xl font-semibold tracking-tight">
            Gantt
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            Timeline bars, milestones and progress fills. Drag a bar (or focus it and use the arrow
            keys) to reschedule the task.
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
        <label htmlFor="gantt-space" className="text-sm font-medium">
          Space
        </label>
        <select
          id="gantt-space"
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
            {formatShortDate(range.start)} – {formatShortDate(range.end)} · drag a bar to reschedule.
            {criticalCount > 0 ? (
              <span className="ml-2 inline-flex items-center gap-1 font-medium text-red-600 dark:text-red-400">
                <span className="size-2 rounded-full bg-red-500" aria-hidden="true" />
                {criticalCount} on the critical path
              </span>
            ) : null}
          </p>
        </header>
        <div className="overflow-x-auto" aria-label="Horizontally scrollable Gantt chart">
          <div className="min-w-[760px]">
            <div className="grid" style={{ gridTemplateColumns: `17rem ${timelineWidth}px` }}>
              <div className="sticky left-0 z-20 border-b border-r border-border bg-card px-4 py-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                Work item
              </div>
              <div className="relative" style={{ width: timelineWidth }}>
                <NonWorkingDaysOverlay
                  rangeStart={range.start}
                  rangeDays={rangeDays}
                  pxPerDay={pxPerDay}
                  workingDaysMask={workingDaysMask}
                  holidayDateKeys={holidayDateKeys}
                />
                <HeaderTicks
                  start={range.start}
                  rangeDays={rangeDays}
                  pxPerDay={pxPerDay}
                  zoom={zoom}
                />
              </div>

              {ganttQuery.isLoading ? (
                <div className="col-span-2 p-4 text-sm text-muted-foreground">
                  Loading Gantt data…
                </div>
              ) : spaces.length === 0 ? (
                <div className="col-span-2 p-4">
                  <EmptyState
                    title="No spaces yet"
                    description="Create a Space and add tasks with dates to see them on this timeline."
                  />
                </div>
              ) : bars.length === 0 ? (
                <div className="col-span-2 p-4">
                  <EmptyState
                    title="Nothing to schedule yet"
                    description="Bars appear here once tasks have a start or due date. Add dates to a task in any list and it shows up on this timeline."
                  />
                </div>
              ) : (
                bars.map((bar) => (
                  <div key={bar.id} className="contents">
                    <div className="sticky left-0 z-10 border-b border-r border-border bg-card p-4">
                      <div className="flex items-start justify-between gap-2">
                        <div>
                          <h3 className="text-sm font-semibold">
                            {bar.title}
                            {bar.isCritical ? (
                              <span
                                className="ml-2 inline-flex items-center rounded-full bg-red-100 px-1.5 py-0.5 text-[0.65rem] font-semibold text-red-700 dark:bg-red-950 dark:text-red-300"
                                title="On the critical path"
                              >
                                Critical
                              </span>
                            ) : null}
                          </h3>
                          <p className="mt-1 text-xs text-muted-foreground">
                            {bar.isMilestone ? "Milestone" : `${bar.progress}% complete`}
                          </p>
                        </div>
                        <div className="flex items-center gap-1">
                          {bar.dependsOn.length > 0 ? (
                            <span className="rounded-full bg-muted px-2 py-0.5 text-[0.7rem] font-medium text-muted-foreground">
                              depends on {bar.dependsOn.length}
                            </span>
                          ) : null}
                          <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            className="text-[0.7rem]"
                            disabled={captureBaseline.isPending}
                            title="Snapshot the current start/due date as this task's baseline"
                            onClick={() => captureBaseline.mutate(bar.id)}
                          >
                            Set baseline
                          </Button>
                        </div>
                      </div>
                    </div>
                    <div
                      className={cn(
                        "relative border-b border-border bg-background",
                        "after:absolute after:inset-y-0 after:left-0 after:w-full after:bg-[repeating-linear-gradient(to_right,transparent_0,transparent_calc(var(--gantt-day)-1px),var(--border)_calc(var(--gantt-day)-1px),var(--border)_var(--gantt-day))]",
                      )}
                      style={
                        {
                          width: timelineWidth,
                          minHeight: 72,
                          "--gantt-day": `${pxPerDay}px`,
                        } as CSSProperties
                      }
                    >
                      <NonWorkingDaysOverlay
                        rangeStart={range.start}
                        rangeDays={rangeDays}
                        pxPerDay={pxPerDay}
                        workingDaysMask={workingDaysMask}
                        holidayDateKeys={holidayDateKeys}
                      />
                      <TimelineBar
                        bar={bar}
                        rangeStart={range.start}
                        pxPerDay={pxPerDay}
                        disabled={reschedule.isPending}
                        onReschedule={(deltaDays) => rescheduleBar(bar, deltaDays)}
                      />
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
