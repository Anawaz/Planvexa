import type { DashboardWidget, WidgetData } from "@/lib/planning/types";
import { cn } from "@/lib/utils";
import { BurndownChart } from "./BurndownChart";
import { formatHours, numberFormatter, percentFormatter } from "./helpers";

function widgetTitle(widget: DashboardWidget) {
  const title = widget.config.title;
  return typeof title === "string" ? title : widget.type;
}

function seriesTotal(data?: WidgetData) {
  return data?.series.reduce((total, item) => total + item.value, 0) ?? 0;
}

function BarList({ data, isPercent }: { data?: WidgetData; isPercent?: boolean }) {
  const max = isPercent ? 100 : Math.max(1, ...(data?.series.map((item) => item.value) ?? []));

  return (
    <div className="space-y-3">
      {data?.series.map((item) => (
        <div key={item.label} className="grid gap-1">
          <div className="flex items-center justify-between gap-3 text-xs">
            <span className="font-medium text-muted-foreground">{item.label}</span>
            <span className="font-semibold">
              {isPercent ? `${numberFormatter.format(item.value)}%` : numberFormatter.format(item.value)}
            </span>
          </div>
          <div className="h-2 rounded-full bg-muted">
            <div
              className="h-2 rounded-full bg-primary"
              style={{ width: `${Math.max(4, (item.value / max) * 100)}%` }}
            />
          </div>
        </div>
      )) ?? <p className="text-sm text-muted-foreground">No widget data.</p>}
    </div>
  );
}

function NumberWidget({ data, suffix }: { data?: WidgetData; suffix?: string }) {
  const value = data?.series[0]?.value ?? 0;

  return (
    <div>
      <p className="text-4xl font-semibold tracking-tight">
        {suffix === "hours" ? formatHours(value) : numberFormatter.format(value)}
      </p>
      <p className="mt-2 text-sm text-muted-foreground">{data?.series[0]?.label ?? "Value"}</p>
    </div>
  );
}

function DonutWidget({ data }: { data?: WidgetData }) {
  const total = Math.max(1, seriesTotal(data));
  const first = data?.series[0]?.value ?? 0;
  const percent = first / total;

  return (
    <div className="flex items-center gap-4">
      <div
        className="grid size-28 place-items-center rounded-full"
        style={{
          background: `conic-gradient(var(--primary) ${percent * 100}%, var(--muted) 0)`,
        }}
        aria-label={`${percentFormatter.format(percent)} ${data?.series[0]?.label ?? "complete"}`}
      >
        <span className="grid size-20 place-items-center rounded-full bg-card text-lg font-semibold">
          {percentFormatter.format(percent)}
        </span>
      </div>
      <div className="space-y-2">
        {data?.series.map((item, index) => (
          <div key={item.label} className="flex items-center gap-2 text-sm">
            <span
              className={cn(
                "size-2.5 rounded-full",
                index === 0 ? "bg-primary" : "bg-muted-foreground",
              )}
            />
            <span className="text-muted-foreground">{item.label}</span>
            <span className="font-semibold">{numberFormatter.format(item.value)}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

export function DashboardWidgetRenderer({
  widget,
  data,
  isLoading,
}: {
  widget: DashboardWidget;
  data?: WidgetData;
  /** True while the dashboard's data query is still in flight — shows a skeleton instead of
   * misreading "no data yet" as "no data at all" (WidgetData only arrives once, for every widget,
   * when dataQuery resolves). */
  isLoading?: boolean;
}) {
  const isNumber = widget.type === "Overdue" || widget.type === "Completed" || widget.type === "CustomFormula";
  const isDonut = widget.type === "SprintProgress";
  const isHours = widget.type === "TimeLogged" && data?.series.length === 1;
  const isBurndown = widget.type === "Burndown";
  // TasksByAssignee/TasksByPriority/CreatedVsCompleted/CustomFieldBreakdown are plain label/value
  // series — same shape Workload/EstimateVsActual already render via BarList below. GoalProgress is
  // the one new type that needs its bars scaled to a 0-100% axis instead of "biggest value wins".
  const isPercentBarList = widget.type === "GoalProgress";

  return (
    <article className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm">
      <header className="mb-4 flex items-start justify-between gap-3">
        <div>
          <h2 className="text-sm font-semibold">{widgetTitle(widget)}</h2>
          <p className="mt-1 text-xs text-muted-foreground">{widget.type}</p>
        </div>
        {isBurndown || isLoading ? null : (
          <span className="rounded-full bg-muted px-2 py-0.5 text-[0.7rem] font-medium text-muted-foreground">
            {data?.series.length ?? 0} rows
          </span>
        )}
      </header>
      {isLoading ? (
        <div className="h-24 animate-pulse rounded-lg bg-muted/70" aria-label="Loading widget data" />
      ) : isBurndown ? (
        <BurndownChart data={data} />
      ) : isDonut ? (
        <DonutWidget data={data} />
      ) : isNumber || isHours ? (
        <NumberWidget data={data} suffix={isHours ? "hours" : undefined} />
      ) : (
        // Velocity/TasksByAssignee/TasksByPriority/CreatedVsCompleted/CustomFieldBreakdown fall through
        // to here too: one labeled bar per series point.
        <BarList data={data} isPercent={isPercentBarList} />
      )}
    </article>
  );
}
