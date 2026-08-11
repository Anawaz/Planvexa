import type { ListEntriesParams, TimeReportParams, TimesheetParams, UtilizationParams } from "./types";

export const timeKeys = {
  all: ["time"] as const,
  activeTimer: () => [...timeKeys.all, "active-timer"] as const,
  policy: () => [...timeKeys.all, "policy"] as const,
  rates: () => [...timeKeys.all, "rates"] as const,
  entriesRoot: () => [...timeKeys.all, "entries"] as const,
  entries: (params: ListEntriesParams) => [...timeKeys.entriesRoot(), params] as const,
  taskEntries: (taskId: string) => [...timeKeys.entriesRoot(), "task", taskId] as const,
  timesheetsRoot: () => [...timeKeys.all, "timesheets"] as const,
  timesheet: (params: TimesheetParams) => [...timeKeys.timesheetsRoot(), params] as const,
  reportsRoot: () => [...timeKeys.all, "reports"] as const,
  report: (params: TimeReportParams) => [...timeKeys.reportsRoot(), params] as const,
  utilization: (params: UtilizationParams) => [...timeKeys.reportsRoot(), "utilization", params] as const,
  tags: () => [...timeKeys.all, "tags"] as const,
  budgetsRoot: () => [...timeKeys.all, "budgets"] as const,
  budgets: () => [...timeKeys.budgetsRoot(), "list"] as const,
  budgetStatus: (id: string, params: { from: string; to: string }) => [...timeKeys.budgetsRoot(), id, "status", params] as const,
};
