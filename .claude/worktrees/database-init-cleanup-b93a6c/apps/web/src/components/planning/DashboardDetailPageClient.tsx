"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useMemo, useState } from "react";
import { Button, buttonStyles } from "@/components/ui/Button";
import {
  getDashboard,
  getDashboardData,
  getPortfolio,
  getPortfolioPdfHref,
  getSpaceDrillDown,
} from "@/lib/planning/client";
import { planningKeys } from "@/lib/planning/queries";
import type { Dashboard, PortfolioRow, WidgetData } from "@/lib/planning/types";
import { useRecordRecentView } from "@/lib/recent/useRecordRecentView";
import { addDays, formatLongDate, numberFormatter } from "./helpers";
import { DashboardWidgetRenderer } from "./DashboardWidgets";

function escapeCsv(value: string | number) {
  const text = String(value);
  return /[",\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

function downloadDashboardCsv(dashboard: Dashboard, data: WidgetData[]) {
  const rows = [
    ["Dashboard", "Widget", "Type", "Label", "Value"],
    ...dashboard.widgets.flatMap((widget) => {
      const widgetData = data.find((item) => item.widgetId === widget.id);
      const title = typeof widget.config.title === "string" ? widget.config.title : widget.type;

      return (widgetData?.series ?? []).map((item) => [
        dashboard.name,
        title,
        widget.type,
        item.label,
        item.value,
      ]);
    }),
  ];
  const csv = rows.map((row) => row.map(escapeCsv).join(",")).join("\n");
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = `${dashboard.name.toLowerCase().replaceAll(/\s+/g, "-")}-widgets.csv`;
  anchor.click();
  URL.revokeObjectURL(url);
}

export function DashboardDetailPageClient({ dashboardId }: { dashboardId: string }) {
  useRecordRecentView("dashboard", dashboardId);
  const [rangeEnd, setRangeEnd] = useState(() => new Date());
  const rangeStart = useMemo(() => addDays(rangeEnd, -29), [rangeEnd]);
  const params = useMemo(
    () => ({ from: rangeStart.toISOString(), to: rangeEnd.toISOString() }),
    [rangeEnd, rangeStart],
  );
  const dashboardQuery = useQuery({
    queryKey: planningKeys.dashboard(dashboardId),
    queryFn: () => getDashboard(dashboardId),
  });
  const dataQuery = useQuery({
    queryKey: planningKeys.dashboardData(dashboardId, params),
    queryFn: () => getDashboardData(dashboardId, params),
  });
  const portfolioQuery = useQuery({
    queryKey: planningKeys.portfolio(),
    queryFn: getPortfolio,
  });
  const [drillDownSpace, setDrillDownSpace] = useState<PortfolioRow | null>(null);
  const drillDownQuery = useQuery({
    queryKey: ["reporting", "drill-down", "space", drillDownSpace?.key],
    queryFn: () => getSpaceDrillDown(drillDownSpace!.key),
    enabled: drillDownSpace !== null,
  });
  const dashboard = dashboardQuery.data;
  const dataByWidget = useMemo(() => {
    return new Map((dataQuery.data ?? []).map((item) => [item.widgetId, item]));
  }, [dataQuery.data]);

  return (
    <section aria-labelledby="dashboard-detail-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Dashboard detail</p>
          <h1 id="dashboard-detail-title" className="mt-2 text-3xl font-semibold tracking-tight">
            {dashboard?.name ?? "Dashboard"}
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            Widgets are rendered from typed series data returned for the selected date range.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Link href="/app/dashboards" className={buttonStyles({ variant: "outline", size: "sm" })}>
            Back to dashboards
          </Link>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => setRangeEnd((current) => addDays(current, -30))}
          >
            Previous 30 days
          </Button>
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={() => setRangeEnd(new Date())}
          >
            Latest
          </Button>
          <Button
            type="button"
            size="sm"
            disabled={!dashboard || !dataQuery.data?.length}
            onClick={() => dashboard && downloadDashboardCsv(dashboard, dataQuery.data ?? [])}
          >
            Export CSV
          </Button>
        </div>
      </div>

      <div className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm">
        <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
          Reporting range
        </p>
        <p className="mt-2 text-lg font-semibold">
          {formatLongDate(rangeStart)} – {formatLongDate(rangeEnd)}
        </p>
      </div>

      {dashboardQuery.isLoading ? (
        <p className="rounded-[var(--radius)] border border-border bg-card p-4 text-sm text-muted-foreground">
          Loading dashboard…
        </p>
      ) : dashboard ? (
        <div className="grid gap-4 xl:grid-cols-3">
          {dashboard.widgets.map((widget) => (
            <DashboardWidgetRenderer
              key={widget.id}
              widget={widget}
              data={dataByWidget.get(widget.id)}
            />
          ))}
        </div>
      ) : (
        <p className="rounded-[var(--radius)] border border-border bg-card p-4 text-sm text-muted-foreground">
          Dashboard not found.
        </p>
      )}

      <section
        className="rounded-[var(--radius)] border border-border bg-card shadow-sm"
        aria-labelledby="portfolio-health-title"
      >
        <header className="border-b border-border p-4">
          <h2 id="portfolio-health-title" className="text-sm font-semibold">
            Portfolio health
          </h2>
          <p className="mt-1 text-xs text-muted-foreground">
            Portfolio data uses a separate report query so dashboard widgets can link to deeper
            reporting later.
          </p>
          <a
            href={getPortfolioPdfHref()}
            className={buttonStyles({ variant: "outline", size: "sm" }) + " mt-3 inline-flex"}
          >
            Export PDF
          </a>
        </header>
        <div className="overflow-x-auto">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-4 py-3 font-semibold">Portfolio</th>
                <th className="px-4 py-3 font-semibold">Tasks</th>
                <th className="px-4 py-3 font-semibold">Completed</th>
                <th className="px-4 py-3 font-semibold">Logged</th>
                <th className="px-4 py-3 font-semibold">Health</th>
                <th className="px-4 py-3 font-semibold">Milestones</th>
                <th className="px-4 py-3 font-semibold">Risks</th>
                <th className="px-4 py-3 font-semibold">Budget</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {(portfolioQuery.data ?? []).map((row) => (
                <tr key={row.key}>
                  <td className="px-4 py-3 font-medium">
                    <button
                      type="button"
                      className="underline decoration-dotted underline-offset-2 hover:text-primary"
                      onClick={() => setDrillDownSpace(row)}
                    >
                      {row.label}
                    </button>
                  </td>
                  <td className="px-4 py-3">{numberFormatter.format(row.totalTasks)}</td>
                  <td className="px-4 py-3">{numberFormatter.format(row.completedTasks)}</td>
                  <td className="px-4 py-3">{numberFormatter.format(row.loggedHours)}h</td>
                  <td className="px-4 py-3">
                    <span className="rounded-full bg-primary/10 px-2.5 py-1 text-xs font-semibold text-primary">
                      {row.healthPercent}%
                    </span>
                  </td>
                  <td className="px-4 py-3">
                    {row.milestones.length === 0
                      ? "—"
                      : `${row.milestones.filter((m) => m.isCompleted).length}/${row.milestones.length}`}
                  </td>
                  <td className="px-4 py-3">
                    {row.risks.length === 0 ? "—" : row.risks.length}
                  </td>
                  <td className="px-4 py-3">
                    {row.budget
                      ? `${row.budget.monetaryConsumedPercent ?? row.budget.timeConsumedPercent ?? 0}%`
                      : "—"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {portfolioQuery.isLoading ? (
            <p className="p-4 text-sm text-muted-foreground">Loading portfolio rows…</p>
          ) : null}
        </div>
      </section>

      {drillDownSpace ? (
        <section
          className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm"
          aria-labelledby="drill-down-title"
        >
          <div className="flex items-center justify-between gap-3">
            <h2 id="drill-down-title" className="text-sm font-semibold">
              Tasks in {drillDownSpace.label}
            </h2>
            <Button type="button" size="sm" variant="ghost" onClick={() => setDrillDownSpace(null)}>
              Close
            </Button>
          </div>
          <p className="mt-1 text-xs text-muted-foreground">
            Permission-filtered: only tasks you can read appear here, even though the count above
            includes every task in the space.
          </p>
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
