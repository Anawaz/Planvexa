"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Button, buttonStyles } from "@/components/ui/Button";
import { getSpaceDrillDown } from "@/lib/planning/client";
import type { PortfolioRow } from "@/lib/planning/types";
import { numberFormatter } from "./helpers";

type PortfolioHealthTableProps = {
  title: string;
  description: string;
  rows: PortfolioRow[];
  isLoading: boolean;
  /** Only the workspace-wide report (GET /reports/portfolio) has a PDF export today. */
  exportPdfHref?: string;
};

/**
 * The per-space Health/Progress/Milestones/Risks/Budget rollup table, plus its click-through task
 * drill-down. Shared by the workspace-wide dashboard report and a curated Portfolio's scoped report
 * (PortfolioService.GetAsync vs GetReportAsync) — same PortfolioRowDto shape either way.
 */
export function PortfolioHealthTable({ title, description, rows, isLoading, exportPdfHref }: PortfolioHealthTableProps) {
  const [drillDownSpace, setDrillDownSpace] = useState<PortfolioRow | null>(null);
  const drillDownQuery = useQuery({
    queryKey: ["reporting", "drill-down", "space", drillDownSpace?.key],
    queryFn: () => getSpaceDrillDown(drillDownSpace!.key),
    enabled: drillDownSpace !== null,
  });

  return (
    <>
      <section
        className="rounded-[var(--radius)] border border-border bg-card shadow-sm"
        aria-labelledby="portfolio-health-title"
      >
        <header className="border-b border-border p-4">
          <h2 id="portfolio-health-title" className="text-sm font-semibold">
            {title}
          </h2>
          <p className="mt-1 text-xs text-muted-foreground">{description}</p>
          {exportPdfHref ? (
            <a href={exportPdfHref} className={buttonStyles({ variant: "outline", size: "sm" }) + " mt-3 inline-flex"}>
              Export PDF
            </a>
          ) : null}
        </header>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[64rem] text-left text-sm">
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
              {rows.map((row) => (
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
                  <td className="px-4 py-3">{row.risks.length === 0 ? "—" : row.risks.length}</td>
                  <td className="px-4 py-3">
                    {row.budget
                      ? `${row.budget.monetaryConsumedPercent ?? row.budget.timeConsumedPercent ?? 0}%`
                      : "—"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {isLoading ? <p className="p-4 text-sm text-muted-foreground">Loading portfolio rows…</p> : null}
          {!isLoading && rows.length === 0 ? (
            <p className="p-4 text-sm text-muted-foreground">No spaces to report on yet.</p>
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
    </>
  );
}
