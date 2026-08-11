"use client";

import { useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { Button } from "@/components/ui/Button";
import { accountingExportHref, getTimeReport, getUtilizationReport, listTimeTags } from "@/lib/time/client";
import { moneyFormatter, toDateInputValue } from "@/lib/time/format";
import { timeKeys } from "@/lib/time/queries";
import type { ReportRow, TimeReportGroupBy } from "@/lib/time/types";
import { useMemberDirectory } from "@/lib/members";

const groupByOptions: Array<{ value: TimeReportGroupBy; label: string }> = [
  // The API calls this grouping "project"; it groups by the task's list, which is the closest
  // thing the domain has to a project, so the button says what it actually does.
  { value: "project", label: "List" },
  { value: "task", label: "Task" },
  { value: "user", label: "User" },
];

function startOfCurrentWeek() {
  const today = new Date();
  const start = new Date(today);
  const diff = (start.getDay() - 1 + 7) % 7;
  start.setDate(start.getDate() - diff);
  start.setHours(0, 0, 0, 0);
  return start;
}

function endOfCurrentWeek() {
  const end = startOfCurrentWeek();
  end.setDate(end.getDate() + 6);
  end.setHours(23, 59, 59, 999);
  return end;
}

function dateRangeToIso(fromDate: string, toDate: string) {
  return {
    from: new Date(`${fromDate}T00:00:00`).toISOString(),
    to: new Date(`${toDate}T23:59:59`).toISOString(),
  };
}

function csvEscape(value: string | number) {
  const raw = String(value);
  return raw.includes(",") || raw.includes('"') || raw.includes("\n")
    ? `"${raw.replaceAll('"', '""')}"`
    : raw;
}

function buildCsv(rows: ReportRow[]) {
  const headers = ["Label", "Hours", "Billable hours", "Cost", "Revenue"];
  const body = rows.map((row) => [row.label, row.hours, row.billableHours, row.cost, row.revenue]);
  return [headers, ...body].map((line) => line.map(csvEscape).join(",")).join("\n");
}

/** Admin-only on the API; a Member sees the error notice instead of a table. */
function UtilizationSection({ from, to }: { from: string; to: string }) {
  const directory = useMemberDirectory();
  const params = useMemo(() => ({ from, to }), [from, to]);
  const utilizationQuery = useQuery({
    queryKey: timeKeys.utilization(params),
    queryFn: () => getUtilizationReport(params),
  });
  const rows = utilizationQuery.data ?? [];

  return (
    <section
      className="overflow-hidden rounded-[var(--radius)] border border-border bg-card shadow-sm"
      aria-labelledby="utilization-title"
    >
      <div className="border-b border-border p-4">
        <h2 id="utilization-title" className="text-sm font-semibold">Utilization</h2>
        <p className="text-xs text-muted-foreground">
          Billable share of tracked hours per member over the same range. Requires administrator access.
        </p>
      </div>
      {utilizationQuery.isError ? (
        <p role="alert" className="m-4 rounded-lg border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300">
          Utilization could not be loaded: {(utilizationQuery.error as Error).message}
        </p>
      ) : (
        <div className="overflow-x-auto">
          <table className="min-w-full border-separate border-spacing-0 text-sm">
            <thead className="bg-muted/60 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th scope="col" className="px-4 py-3 font-semibold">Member</th>
                <th scope="col" className="px-4 py-3 font-semibold">Tracked</th>
                <th scope="col" className="px-4 py-3 font-semibold">Billable</th>
                <th scope="col" className="px-4 py-3 font-semibold">Utilization</th>
              </tr>
            </thead>
            <tbody>
              {utilizationQuery.isLoading ? (
                <tr>
                  <td colSpan={4} className="px-4 py-6 text-muted-foreground">Loading utilization…</td>
                </tr>
              ) : rows.length === 0 ? (
                <tr>
                  <td colSpan={4} className="px-4 py-6 text-muted-foreground">No tracked time in this range.</td>
                </tr>
              ) : (
                rows.map((row) => (
                  <tr key={row.userId}>
                    <th scope="row" className="border-b border-border px-4 py-3 text-left font-medium">
                      {directory.getLabel(row.userId)}
                    </th>
                    <td className="border-b border-border px-4 py-3">{row.trackedHours.toFixed(2)}</td>
                    <td className="border-b border-border px-4 py-3">{row.billableHours.toFixed(2)}</td>
                    <td className="border-b border-border px-4 py-3">{row.utilizationPercent.toFixed(2)}%</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

export default function TimeReportPage() {
  const [groupBy, setGroupBy] = useState<TimeReportGroupBy>("task");
  const [fromDate, setFromDate] = useState(() => toDateInputValue(startOfCurrentWeek()));
  const [toDate, setToDate] = useState(() => toDateInputValue(endOfCurrentWeek()));
  const [tagId, setTagId] = useState<string | undefined>(undefined);
  const tagsQuery = useQuery({ queryKey: timeKeys.tags(), queryFn: listTimeTags });
  const reportParams = useMemo(
    () => ({ groupBy, tagId, ...dateRangeToIso(fromDate, toDate) }),
    [fromDate, groupBy, tagId, toDate],
  );
  const reportQuery = useQuery({
    queryKey: timeKeys.report(reportParams),
    queryFn: () => getTimeReport(reportParams),
  });
  const rows = reportQuery.data ?? [];
  const totals = rows.reduce(
    (total, row) => ({
      hours: total.hours + row.hours,
      billableHours: total.billableHours + row.billableHours,
      cost: total.cost + row.cost,
      revenue: total.revenue + row.revenue,
    }),
    { hours: 0, billableHours: 0, cost: 0, revenue: 0 },
  );

  function exportCsv() {
    const csv = buildCsv(rows);
    const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `planvexa-time-report-${groupBy}-${fromDate}-to-${toDate}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  }

  return (
    <section aria-labelledby="time-report-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Time analytics</p>
          <h1 id="time-report-title" className="mt-2 text-3xl font-semibold tracking-tight">
            Time report
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            Group tracked hours by project, task, or user and export the current results to CSV.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button type="button" variant="outline" onClick={exportCsv} disabled={rows.length === 0}>
            Export CSV
          </Button>
          <a
            href={accountingExportHref(reportParams)}
            className="inline-flex h-11 items-center rounded-lg border border-border bg-card px-4 text-sm font-medium hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            title="QuickBooks Online (Transaction Pro Importer) time-activity CSV layout. Requires administrator access."
          >
            Export accounting CSV
          </a>
        </div>
      </div>

      {reportQuery.isError ? (
        <p
          role="alert"
          className="rounded-[var(--radius)] border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
        >
          The time report could not be loaded: {(reportQuery.error as Error).message}
        </p>
      ) : null}

      <section className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm" aria-label="Report filters">
        <div className="grid gap-4 lg:grid-cols-[1.2fr_1fr_1fr_1fr] lg:items-end">
          <fieldset>
            <legend className="mb-2 text-sm font-semibold">Group by</legend>
            <div className="inline-flex rounded-xl border border-border bg-background p-1 shadow-sm">
              {groupByOptions.map((option) => (
                <Button
                  key={option.value}
                  type="button"
                  size="sm"
                  variant={groupBy === option.value ? "primary" : "ghost"}
                  aria-pressed={groupBy === option.value}
                  onClick={() => setGroupBy(option.value)}
                >
                  {option.label}
                </Button>
              ))}
            </div>
          </fieldset>
          <label className="grid gap-2 text-sm font-medium">
            From
            <input
              type="date"
              value={fromDate}
              className="rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              onChange={(event) => setFromDate(event.target.value)}
            />
          </label>
          <label className="grid gap-2 text-sm font-medium">
            To
            <input
              type="date"
              value={toDate}
              className="rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              onChange={(event) => setToDate(event.target.value)}
            />
          </label>
          <label className="grid gap-2 text-sm font-medium">
            Tag
            <select
              value={tagId ?? ""}
              className="h-11 rounded-lg border border-border bg-background px-3 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              onChange={(event) => setTagId(event.target.value || undefined)}
            >
              <option value="">All tags</option>
              {(tagsQuery.data ?? []).map((tag) => (
                <option key={tag.id} value={tag.id}>
                  {tag.name}
                </option>
              ))}
            </select>
          </label>
        </div>
      </section>

      <section className="overflow-hidden rounded-[var(--radius)] border border-border bg-card shadow-sm" aria-labelledby="report-table-title">
        <div className="border-b border-border p-4">
          <h2 id="report-table-title" className="text-sm font-semibold">Report rows</h2>
          <p className="text-xs text-muted-foreground">Money is modeled as numbers and formatted with Intl.NumberFormat.</p>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-full border-separate border-spacing-0 text-sm">
            <thead className="bg-muted/60 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th scope="col" className="px-4 py-3 font-semibold">Label</th>
                <th scope="col" className="px-4 py-3 font-semibold">Hours</th>
                <th scope="col" className="px-4 py-3 font-semibold">Billable</th>
                <th scope="col" className="px-4 py-3 font-semibold">Cost</th>
                <th scope="col" className="px-4 py-3 font-semibold">Revenue</th>
              </tr>
            </thead>
            <tbody>
              {reportQuery.isLoading ? (
                <tr>
                  <td colSpan={5} className="px-4 py-6 text-muted-foreground">Loading report…</td>
                </tr>
              ) : rows.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-4 py-6 text-muted-foreground">No time entries in this range.</td>
                </tr>
              ) : (
                rows.map((row) => (
                  <tr key={row.key} className="border-b border-border">
                    <th scope="row" className="border-b border-border px-4 py-3 text-left font-medium">
                      {row.label}
                    </th>
                    <td className="border-b border-border px-4 py-3">{row.hours.toFixed(2)}</td>
                    <td className="border-b border-border px-4 py-3">{row.billableHours.toFixed(2)}</td>
                    <td className="border-b border-border px-4 py-3">{moneyFormatter.format(row.cost)}</td>
                    <td className="border-b border-border px-4 py-3">{moneyFormatter.format(row.revenue)}</td>
                  </tr>
                ))
              )}
            </tbody>
            <tfoot className="bg-muted/40 font-semibold">
              <tr>
                <th scope="row" className="px-4 py-3 text-left">Totals</th>
                <td className="px-4 py-3">{totals.hours.toFixed(2)}</td>
                <td className="px-4 py-3">{totals.billableHours.toFixed(2)}</td>
                <td className="px-4 py-3">{moneyFormatter.format(totals.cost)}</td>
                <td className="px-4 py-3">{moneyFormatter.format(totals.revenue)}</td>
              </tr>
            </tfoot>
          </table>
        </div>
      </section>

      <UtilizationSection from={reportParams.from} to={reportParams.to} />
    </section>
  );
}
