"use client";

import type { GanttBar } from "@/lib/planning/types";
import { addDays, dayKey, formatShortDate } from "./helpers";

/**
 * Date-range bar-rendering primitives shared by the Gantt view (GanttPageClient.tsx, dependency
 * arrows + critical path + drag-to-reschedule) and the simpler Timeline view (TimelinePageClient.tsx,
 * swimlanes, no dependency/critical-path complexity). Extracted here so Timeline reuses the
 * same date-math instead of re-deriving it.
 */

export type Zoom = "day" | "week";

export const MS_PER_DAY = 86_400_000;

export function diffDays(from: Date, to: Date) {
  return Math.round((to.getTime() - from.getTime()) / MS_PER_DAY);
}

export function normalizeDate(value: string) {
  const date = new Date(value);
  return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()));
}

export function getBarDates(bar: Pick<GanttBar, "startDate" | "dueDate">) {
  const start = bar.startDate ?? bar.dueDate ?? new Date().toISOString();
  const end = bar.dueDate ?? bar.startDate ?? start;
  return { start: normalizeDate(start), end: normalizeDate(end) };
}

export function getTimelineRange(bars: Array<Pick<GanttBar, "startDate" | "dueDate">>) {
  if (bars.length === 0) {
    const today = normalizeDate(new Date().toISOString());
    return { start: addDays(today, -2), end: addDays(today, 30) };
  }

  const dates = bars.flatMap((bar) => {
    const { start, end } = getBarDates(bar);
    return [start, end];
  });
  const min = new Date(Math.min(...dates.map((date) => date.getTime())));
  const max = new Date(Math.max(...dates.map((date) => date.getTime())));

  return { start: addDays(min, -2), end: addDays(max, 3) };
}

export function HeaderTicks({
  start,
  rangeDays,
  pxPerDay,
  zoom,
}: {
  start: Date;
  rangeDays: number;
  pxPerDay: number;
  zoom: Zoom;
}) {
  const step = zoom === "day" ? 1 : 7;
  const ticks = Array.from({ length: Math.ceil(rangeDays / step) }, (_, index) => ({
    date: addDays(start, index * step),
    width: step * pxPerDay,
  }));

  return (
    <div className="sticky top-0 z-10 flex border-b border-border bg-muted/80 text-xs font-medium text-muted-foreground">
      {ticks.map((tick) => (
        <div key={dayKey(tick.date)} className="border-r border-border px-2 py-2" style={{ width: tick.width }}>
          {zoom === "day"
            ? new Intl.DateTimeFormat("en", { day: "numeric", month: "short" }).format(tick.date)
            : `Week of ${formatShortDate(tick.date)}`}
        </div>
      ))}
    </div>
  );
}
