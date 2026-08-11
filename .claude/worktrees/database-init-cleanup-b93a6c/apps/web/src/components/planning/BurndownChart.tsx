import type { WidgetData } from "@/lib/planning/types";

type DayPoint = { day: string; remaining: number; completed: number };

/**
 * Parses WidgetComputer.BurndownAsync's flat SeriesPointDto[] (two points per day,
 * labeled "yyyy-MM-dd — remaining" / "yyyy-MM-dd — completed") back into a day-by-day series. No charting
 * library dependency (AGENTS.md rule 16 — package.json has none, and this is one small line chart), just a
 * hand-rolled SVG polyline.
 */
function parseSeries(series: WidgetData["series"]): DayPoint[] {
  const byDay = new Map<string, Partial<DayPoint>>();
  for (const point of series) {
    const [day, kind] = point.label.split(" — ");
    if (!day || !kind) continue;
    const entry = byDay.get(day) ?? {};
    if (kind === "remaining") entry.remaining = point.value;
    if (kind === "completed") entry.completed = point.value;
    byDay.set(day, entry);
  }

  return [...byDay.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([day, entry]) => ({ day, remaining: entry.remaining ?? 0, completed: entry.completed ?? 0 }));
}

const WIDTH = 320;
const HEIGHT = 160;
const PADDING = 24;

function toPath(values: number[], max: number) {
  if (values.length === 0) return "";
  const stepX = values.length > 1 ? (WIDTH - PADDING * 2) / (values.length - 1) : 0;
  return values
    .map((value, index) => {
      const x = PADDING + index * stepX;
      const y = HEIGHT - PADDING - (max === 0 ? 0 : (value / max) * (HEIGHT - PADDING * 2));
      return `${index === 0 ? "M" : "L"}${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(" ");
}

export function BurndownChart({ data }: { data?: WidgetData }) {
  const days = parseSeries(data?.series ?? []);
  if (days.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        No sprint selected — configure the widget&apos;s sprintId.
      </p>
    );
  }

  const max = Math.max(1, ...days.map((d) => Math.max(d.remaining, d.completed)));
  const remainingPath = toPath(
    days.map((d) => d.remaining),
    max,
  );
  const completedPath = toPath(
    days.map((d) => d.completed),
    max,
  );

  return (
    <div>
      <svg viewBox={`0 0 ${WIDTH} ${HEIGHT}`} className="w-full" role="img" aria-label="Sprint burndown/burnup chart">
        <line
          x1={PADDING}
          y1={HEIGHT - PADDING}
          x2={WIDTH - PADDING}
          y2={HEIGHT - PADDING}
          stroke="var(--border)"
          strokeWidth={1}
        />
        <path d={remainingPath} fill="none" stroke="var(--primary)" strokeWidth={2} />
        <path d={completedPath} fill="none" stroke="var(--muted-foreground)" strokeWidth={2} strokeDasharray="4 3" />
      </svg>
      <div className="mt-2 flex items-center gap-4 text-xs text-muted-foreground">
        <span className="flex items-center gap-1.5">
          <span className="h-0.5 w-4 bg-[var(--primary)]" /> Remaining
        </span>
        <span className="flex items-center gap-1.5">
          <span className="h-0.5 w-4 border-t border-dashed border-[var(--muted-foreground)]" /> Completed
        </span>
        <span className="ml-auto">
          {days[0]?.day} – {days[days.length - 1]?.day}
        </span>
      </div>
    </div>
  );
}
