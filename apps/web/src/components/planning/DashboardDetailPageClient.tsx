"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useEffect, useMemo, useRef, useState } from "react";
import { Button, buttonStyles } from "@/components/ui/Button";
import { QueryState } from "@/components/ui/QueryState";
import {
  createScheduledReport,
  deleteScheduledReport,
  getDashboard,
  getDashboardData,
  getDashboardExportXlsxHref,
  getPortfolio,
  getPortfolioPdfHref,
  listScheduledReports,
  setScheduledReportEnabled,
  updateDashboard,
} from "@/lib/planning/client";
import { planningKeys } from "@/lib/planning/queries";
import type {
  Dashboard,
  DashboardWidgetType,
  ScheduledReportCadence,
  WidgetData,
} from "@/lib/planning/types";
import { useMembers } from "@/lib/members";
import { useRecordRecentView } from "@/lib/recent/useRecordRecentView";
import { addDays, formatLongDate } from "./helpers";
import { CustomFieldPicker } from "./CustomFieldPicker";
import { DashboardWidgetRenderer } from "./DashboardWidgets";
import { PortfolioHealthTable } from "./PortfolioHealthTable";
import { SprintPicker } from "./SprintPicker";

// All widget types the reporting engine can compute (WidgetComputer.cs), offered in the "add widget"
// picker. Burndown needs a sprint (via SprintPicker, never a raw id), CustomFormula needs a formula
// string, CustomFieldBreakdown needs a custom field (via CustomFieldPicker, never a raw id); every
// other type only needs an optional display title.
const WIDGET_TYPES: DashboardWidgetType[] = [
  "TasksByStatus",
  "Overdue",
  "Completed",
  "TimeLogged",
  "BillableTotals",
  "Workload",
  "EstimateVsActual",
  "SprintProgress",
  "PortfolioHealth",
  "Burndown",
  "CustomFormula",
  "Velocity",
  "TasksByAssignee",
  "TasksByPriority",
  "CreatedVsCompleted",
  "GoalProgress",
  "CustomFieldBreakdown",
];

type DraftWidget = { key: string; type: DashboardWidgetType; config: Record<string, unknown> };

function newDraftKey() {
  return typeof crypto !== "undefined" && "randomUUID" in crypto
    ? crypto.randomUUID()
    : `draft-${Math.random().toString(36).slice(2)}`;
}

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
  const dashboard = dashboardQuery.data;
  const dataByWidget = useMemo(() => {
    return new Map((dataQuery.data ?? []).map((item) => [item.widgetId, item]));
  }, [dataQuery.data]);

  // ---- Widget editor: add/remove/reorder/reconfigure, saved as the full widgets array via PATCH. ----
  const queryClient = useQueryClient();
  const [draftWidgets, setDraftWidgets] = useState<DraftWidget[]>([]);
  const [newWidgetType, setNewWidgetType] = useState<DashboardWidgetType>("TasksByStatus");
  const initializedRef = useRef(false);
  useEffect(() => {
    if (!initializedRef.current && dashboard) {
      setDraftWidgets(dashboard.widgets.map((w) => ({ key: w.id, type: w.type, config: w.config })));
      initializedRef.current = true;
    }
  }, [dashboard]);

  const saveWidgetsMutation = useMutation({
    mutationFn: (widgets: DraftWidget[]) =>
      updateDashboard(dashboardId, { widgets: widgets.map((w) => ({ type: w.type, config: w.config })) }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: planningKeys.dashboard(dashboardId) });
      void queryClient.invalidateQueries({ queryKey: planningKeys.dashboards() });
    },
  });

  function addWidget() {
    setDraftWidgets((current) => [...current, { key: newDraftKey(), type: newWidgetType, config: {} }]);
  }

  function removeWidget(key: string) {
    setDraftWidgets((current) => current.filter((w) => w.key !== key));
  }

  function moveWidget(index: number, direction: -1 | 1) {
    setDraftWidgets((current) => {
      const target = index + direction;
      if (target < 0 || target >= current.length) {
        return current;
      }

      const next = [...current];
      [next[index], next[target]] = [next[target], next[index]];
      return next;
    });
  }

  function updateWidgetConfig(key: string, patch: Record<string, unknown>) {
    setDraftWidgets((current) =>
      current.map((w) => (w.key === key ? { ...w, config: { ...w.config, ...patch } } : w)),
    );
  }

  // ---- Scheduled reports: periodic email export of this dashboard to a chosen recipient list. ----
  const scheduledReportsQuery = useQuery({
    queryKey: planningKeys.scheduledReports(),
    queryFn: listScheduledReports,
  });
  const dashboardScheduledReports = (scheduledReportsQuery.data ?? []).filter(
    (r) => r.dashboardId === dashboardId,
  );
  const { data: members } = useMembers();
  const [recipientEmails, setRecipientEmails] = useState<Set<string>>(new Set());
  const [cadence, setCadence] = useState<ScheduledReportCadence>("Daily");

  const createScheduledReportMutation = useMutation({
    mutationFn: () =>
      createScheduledReport({ dashboardId, recipients: [...recipientEmails], cadence }),
    onSuccess: () => {
      setRecipientEmails(new Set());
      void queryClient.invalidateQueries({ queryKey: planningKeys.scheduledReports() });
    },
  });

  const setScheduledReportEnabledMutation = useMutation({
    mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) => setScheduledReportEnabled(id, enabled),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: planningKeys.scheduledReports() }),
  });

  const deleteScheduledReportMutation = useMutation({
    mutationFn: (id: string) => deleteScheduledReport(id),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: planningKeys.scheduledReports() }),
  });

  function toggleRecipient(email: string) {
    setRecipientEmails((current) => {
      const next = new Set(current);
      if (next.has(email)) {
        next.delete(email);
      } else {
        next.add(email);
      }
      return next;
    });
  }

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
          {dashboard ? (
            <a
              href={getDashboardExportXlsxHref(dashboard.id, params)}
              className={buttonStyles({ size: "sm" })}
            >
              Export Excel
            </a>
          ) : null}
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

      <QueryState query={dashboardQuery} loadingLabel="Loading dashboard…">
        {dashboard ? (
        <div className="grid gap-4 xl:grid-cols-3">
          {dashboard.widgets.map((widget) => (
            <DashboardWidgetRenderer
              key={widget.id}
              widget={widget}
              data={dataByWidget.get(widget.id)}
              isLoading={dataQuery.isLoading}
            />
          ))}
        </div>
      ) : (
        <p className="rounded-[var(--radius)] border border-border bg-card p-4 text-sm text-muted-foreground">
          Dashboard not found.
        </p>
      )}
      </QueryState>

      <section
        className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm"
        aria-labelledby="widget-editor-title"
      >
        <div className="flex items-center justify-between gap-3">
          <h2 id="widget-editor-title" className="text-sm font-semibold">
            Widgets
          </h2>
          <Button
            type="button"
            size="sm"
            disabled={saveWidgetsMutation.isPending}
            onClick={() => saveWidgetsMutation.mutate(draftWidgets)}
          >
            Save changes
          </Button>
        </div>
        {saveWidgetsMutation.error ? (
          <p role="alert" className="mt-2 text-sm text-red-700 dark:text-red-400">
            Could not save widgets: {(saveWidgetsMutation.error as Error).message}
          </p>
        ) : null}

        <ul className="mt-4 divide-y divide-border">
          {draftWidgets.map((widget, index) => (
            <li key={widget.key} className="grid gap-2 py-3 first:pt-0 last:pb-0">
              <div className="flex flex-wrap items-center gap-2">
                <span className="rounded-full bg-muted px-2 py-0.5 text-xs font-medium">{widget.type}</span>
                <label className="sr-only" htmlFor={`widget-title-${widget.key}`}>
                  Widget title
                </label>
                <input
                  id={`widget-title-${widget.key}`}
                  value={typeof widget.config.title === "string" ? widget.config.title : ""}
                  placeholder="Display title (optional)"
                  onChange={(event) => updateWidgetConfig(widget.key, { title: event.target.value })}
                  className="h-9 min-w-0 flex-1 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                />
                <Button
                  type="button"
                  size="sm"
                  variant="ghost"
                  disabled={index === 0}
                  onClick={() => moveWidget(index, -1)}
                >
                  Move up
                </Button>
                <Button
                  type="button"
                  size="sm"
                  variant="ghost"
                  disabled={index === draftWidgets.length - 1}
                  onClick={() => moveWidget(index, 1)}
                >
                  Move down
                </Button>
                <Button
                  type="button"
                  size="sm"
                  variant="ghost"
                  className="text-red-600 hover:text-red-700 dark:text-red-400"
                  onClick={() => removeWidget(widget.key)}
                >
                  Remove
                </Button>
              </div>

              {widget.type === "Burndown" ? (
                <div className="max-w-sm">
                  <label className="sr-only" htmlFor={`widget-sprint-${widget.key}`}>
                    Sprint
                  </label>
                  <SprintPicker
                    id={`widget-sprint-${widget.key}`}
                    value={typeof widget.config.sprintId === "string" ? widget.config.sprintId : ""}
                    onChange={(sprintId) => updateWidgetConfig(widget.key, { sprintId })}
                  />
                </div>
              ) : null}

              {widget.type === "CustomFieldBreakdown" ? (
                <div className="max-w-sm">
                  <label className="sr-only" htmlFor={`widget-custom-field-${widget.key}`}>
                    Custom field
                  </label>
                  <CustomFieldPicker
                    id={`widget-custom-field-${widget.key}`}
                    value={typeof widget.config.customFieldId === "string" ? widget.config.customFieldId : ""}
                    onChange={(customFieldId) => updateWidgetConfig(widget.key, { customFieldId })}
                  />
                </div>
              ) : null}

              {widget.type === "CustomFormula" ? (
                <div className="max-w-sm">
                  <label className="sr-only" htmlFor={`widget-formula-${widget.key}`}>
                    Formula
                  </label>
                  <input
                    id={`widget-formula-${widget.key}`}
                    value={typeof widget.config.formula === "string" ? widget.config.formula : ""}
                    placeholder="e.g. SUM(hours) / COUNT(tasks)"
                    onChange={(event) => updateWidgetConfig(widget.key, { formula: event.target.value })}
                    className="h-9 w-full rounded-lg border border-border bg-background px-3 text-sm font-mono focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                  />
                </div>
              ) : null}
            </li>
          ))}
          {draftWidgets.length === 0 ? (
            <li className="py-3 text-sm text-muted-foreground">No widgets yet — add one below.</li>
          ) : null}
        </ul>

        <div className="mt-4 flex flex-wrap items-center gap-2 border-t border-border pt-4">
          <label className="sr-only" htmlFor="new-widget-type">
            Widget type
          </label>
          <select
            id="new-widget-type"
            value={newWidgetType}
            onChange={(event) => setNewWidgetType(event.target.value as DashboardWidgetType)}
            className="h-9 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          >
            {WIDGET_TYPES.map((type) => (
              <option key={type} value={type}>
                {type}
              </option>
            ))}
          </select>
          <Button type="button" size="sm" variant="outline" onClick={addWidget}>
            Add widget
          </Button>
        </div>
      </section>

      <section
        className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm"
        aria-labelledby="scheduled-reports-title"
      >
        <h2 id="scheduled-reports-title" className="text-sm font-semibold">
          Scheduled reports
        </h2>
        <p className="mt-1 text-xs text-muted-foreground">
          Emails this dashboard&apos;s export to the chosen recipients on a schedule.
        </p>

        <ul className="mt-4 divide-y divide-border">
          {dashboardScheduledReports.map((report) => (
            <li key={report.id} className="flex flex-wrap items-center justify-between gap-2 py-3 first:pt-0 last:pb-0">
              <div>
                <p className="text-sm font-medium">{report.recipients.join(", ")}</p>
                <p className="text-xs text-muted-foreground">
                  {report.cadence}
                  {report.lastSentAtUtc ? ` · last sent ${new Date(report.lastSentAtUtc).toLocaleDateString()}` : ""}
                </p>
              </div>
              <div className="flex items-center gap-2">
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  disabled={setScheduledReportEnabledMutation.isPending}
                  onClick={() =>
                    setScheduledReportEnabledMutation.mutate({ id: report.id, enabled: !report.isEnabled })
                  }
                >
                  {report.isEnabled ? "Disable" : "Enable"}
                </Button>
                <Button
                  type="button"
                  size="sm"
                  variant="ghost"
                  className="text-red-600 hover:text-red-700 dark:text-red-400"
                  disabled={deleteScheduledReportMutation.isPending}
                  onClick={() => deleteScheduledReportMutation.mutate(report.id)}
                >
                  Delete
                </Button>
              </div>
            </li>
          ))}
          {dashboardScheduledReports.length === 0 ? (
            <li className="py-3 text-sm text-muted-foreground">No scheduled reports yet.</li>
          ) : null}
        </ul>

        <div className="mt-4 border-t border-border pt-4">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Recipients</p>
          <ul className="mt-2 flex flex-wrap gap-3">
            {(members ?? []).map((member) =>
              member.email ? (
                <li key={member.userId}>
                  <label className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={recipientEmails.has(member.email)}
                      onChange={() => toggleRecipient(member.email!)}
                    />
                    {member.displayName ?? member.email}
                  </label>
                </li>
              ) : null,
            )}
          </ul>
          {(members ?? []).every((m) => !m.email) ? (
            <p className="mt-2 text-xs text-muted-foreground">No workspace members with an email on file.</p>
          ) : null}

          <div className="mt-3 flex flex-wrap items-center gap-2">
            <label className="sr-only" htmlFor="scheduled-report-cadence">
              Cadence
            </label>
            <select
              id="scheduled-report-cadence"
              value={cadence}
              onChange={(event) => setCadence(event.target.value as ScheduledReportCadence)}
              className="h-9 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            >
              <option value="Daily">Daily</option>
              <option value="Weekly">Weekly</option>
            </select>
            <Button
              type="button"
              size="sm"
              disabled={recipientEmails.size === 0 || createScheduledReportMutation.isPending}
              onClick={() => createScheduledReportMutation.mutate()}
            >
              Schedule report
            </Button>
          </div>
          {createScheduledReportMutation.error ? (
            <p role="alert" className="mt-2 text-sm text-red-700 dark:text-red-400">
              Could not schedule report: {(createScheduledReportMutation.error as Error).message}
            </p>
          ) : null}
        </div>
      </section>

      <PortfolioHealthTable
        title="Portfolio health"
        description="Portfolio data uses a separate report query so dashboard widgets can link to deeper reporting later."
        rows={portfolioQuery.data ?? []}
        isLoading={portfolioQuery.isLoading}
        exportPdfHref={getPortfolioPdfHref()}
      />
    </section>
  );
}
