import { apiClient, proxyHref } from "../api-client";
import type {
  AddSprintItemInput,
  CalendarQuery,
  CalendarTask,
  CreateDashboardInput,
  CreateHolidayInput,
  CreateLeaveInput,
  CreatePortfolioInput,
  CreateScheduledReportInput,
  CreateSprintInput,
  Dashboard,
  DashboardDataQuery,
  DashboardSummary,
  DashboardWidget,
  DrillDownTask,
  GanttBar,
  GanttQuery,
  Holiday,
  LeaveEntry,
  Portfolio,
  PortfolioRow,
  ScheduledReport,
  Sprint,
  SprintBoardColumn,
  SprintStatus,
  TeamWorkloadRow,
  UpdateDashboardInput,
  UpdatePortfolioInput,
  UpdateSprintInput,
  WidgetData,
  WorkloadQuery,
  WorkloadRow,
  WorkSchedule,
} from "./types";

// API calls to backend

export async function getCalendar(params: CalendarQuery) {
  const query = new URLSearchParams();
  query.append("from", params.from);
  query.append("to", params.to);
  if (params.scopeId) query.append("scopeId", params.scopeId);
  const path = `/api/v1/views/calendar?${query.toString()}`;
  return apiClient.get<CalendarTask[]>(path);
}

export async function getGantt(params: GanttQuery) {
  const query = new URLSearchParams();
  query.append("spaceId", params.spaceId);
  const path = `/api/v1/views/gantt?${query.toString()}`;
  return apiClient.get<GanttBar[]>(path);
}

export async function getWorkload(params: WorkloadQuery) {
  const query = new URLSearchParams();
  query.append("from", params.from);
  query.append("to", params.to);
  const path = `/api/v1/views/workload?${query.toString()}`;
  return apiClient.get<WorkloadRow[]>(path);
}

/**  . uc(T)eam view -- workload grouped by Team instead of flat per-individual. */
export async function getTeamWorkload(params: WorkloadQuery) {
  const query = new URLSearchParams();
  query.append("from", params.from);
  query.append("to", params.to);
  const path = `/api/v1/views/team?${query.toString()}`;
  return apiClient.get<TeamWorkloadRow[]>(path);
}

export async function getWorkSchedule() {
  return apiClient.get<WorkSchedule>("/api/v1/planning/work-schedule");
}

export async function setWorkSchedule(schedule: WorkSchedule) {
  return apiClient.put<WorkSchedule>("/api/v1/planning/work-schedule", schedule);
}

export async function listHolidays() {
  return apiClient.get<Holiday[]>("/api/v1/planning/holidays");
}

export async function addHoliday(input: CreateHolidayInput) {
  const body = {
    dateUtc: input.dateUtc,
    name: input.name,
  };
  return apiClient.post<Holiday>("/api/v1/planning/holidays", body);
}

export async function removeHoliday(id: string) {
  await apiClient.delete<void>(`/api/v1/planning/holidays/${id}`);
}

export async function listLeave(params: { userId?: string } = {}) {
  const query = new URLSearchParams();
  if (params.userId) query.append("userId", params.userId);
  const path = `/api/v1/planning/leave${query.toString() ? `?${query.toString()}` : ""}`;
  return apiClient.get<LeaveEntry[]>(path);
}

export async function addLeave(input: CreateLeaveInput) {
  const body = {
    userId: input.userId,
    startUtc: input.startUtc,
    endUtc: input.endUtc,
    type: input.type,
  };
  return apiClient.post<LeaveEntry>("/api/v1/planning/leave", body);
}

export async function removeLeave(id: string) {
  await apiClient.delete<void>(`/api/v1/planning/leave/${id}`);
}

export async function listSprints() {
  return apiClient.get<Sprint[]>("/api/v1/sprints");
}

export async function createSprint(input: CreateSprintInput) {
  const body = {
    name: input.name,
    startUtc: input.startUtc,
    endUtc: input.endUtc,
    goal: input.goal,
  };
  return apiClient.post<Sprint>("/api/v1/sprints", body);
}

export async function updateSprint(id: string, input: UpdateSprintInput) {
  return apiClient.patch<Sprint>(`/api/v1/sprints/${id}`, input);
}

export async function deleteSprint(id: string) {
  await apiClient.delete<void>(`/api/v1/sprints/${id}`);
}

export async function changeSprintStatus(id: string, status: SprintStatus) {
  return apiClient.patch<Sprint>(`/api/v1/sprints/${id}/status`, { status });
}

export async function getSprintBoard(id: string) {
  // The API returns SprintBoardDto ({ sprintId, name, columns }), not a bare array.
  const board = await apiClient.get<{ columns: SprintBoardColumn[] }>(`/api/v1/sprints/${id}/board`);
  return board.columns;
}

export async function addSprintItem(id: string, input: AddSprintItemInput) {
  const body = {
    taskId: input.taskId,
    points: input.points,
  };
  return apiClient.post<void>(`/api/v1/sprints/${id}/items`, body);
}

export async function removeSprintItem(id: string, taskId: string) {
  await apiClient.delete<void>(`/api/v1/sprints/${id}/items/${taskId}`);
}

/** Moves every not-done item out of `sourceId` into `targetId` (e.g. closing out a completed sprint). */
export async function carryOverSprint(sourceId: string, targetId: string) {
  await apiClient.post<void>(`/api/v1/sprints/${sourceId}/carry-over/${targetId}`);
}

// The API's WidgetDto carries `configJson` (a JSON-encoded string), not a parsed `config` object --
// the write side already sends configJson (below); this mirrors it for reads.
type WireWidget = { id: string; type: DashboardWidget["type"]; configJson: string };
type WireDashboard = Omit<Dashboard, "widgets"> & { widgets: WireWidget[] };

function parseDashboard(raw: WireDashboard): Dashboard {
  return {
    ...raw,
    widgets: raw.widgets.map((w) => ({
      id: w.id,
      type: w.type,
      config: w.configJson ? (JSON.parse(w.configJson) as Record<string, unknown>) : {},
    })),
  };
}

export async function listDashboards() {
  return apiClient.get<DashboardSummary[]>("/api/v1/dashboards");
}

export async function getDashboard(id: string) {
  return parseDashboard(await apiClient.get<WireDashboard>(`/api/v1/dashboards/${id}`));
}

export async function createDashboard(input: CreateDashboardInput) {
  const body = {
    name: input.name,
    isPrivate: input.isPrivate,
    widgets: input.widgets.map((w) => ({
      type: w.type,
      configJson: JSON.stringify(w.config),
      position: 0,
    })),
  };
  return parseDashboard(await apiClient.post<WireDashboard>("/api/v1/dashboards", body));
}

/** Partial update: omitted fields keep their stored value, so widgets survive a plain rename. */
export async function updateDashboard(id: string, input: UpdateDashboardInput) {
  const raw = await apiClient.patch<WireDashboard>(`/api/v1/dashboards/${id}`, {
    name: input.name,
    isPrivate: input.isPrivate,
    widgets: input.widgets?.map((w, index) => ({
      type: w.type,
      configJson: JSON.stringify(w.config),
      position: index,
    })),
  });
  return parseDashboard(raw);
}

export async function deleteDashboard(id: string) {
  await apiClient.delete<void>(`/api/v1/dashboards/${id}`);
}

export async function getDashboardData(id: string, params: DashboardDataQuery) {
  const query = new URLSearchParams();
  query.append("from", params.from);
  query.append("to", params.to);
  const path = `/api/v1/dashboards/${id}/data?${query.toString()}`;
  return apiClient.get<WidgetData[]>(path);
}

/** Browser download link (BFF proxy, so the session cookie authenticates it) for a dashboard's Excel export. */
export function getDashboardExportXlsxHref(id: string, params: DashboardDataQuery) {
  return proxyHref(`/dashboards/${id}/export.xlsx`, { from: params.from, to: params.to });
}

export async function getPortfolio() {
  return apiClient.get<PortfolioRow[]>("/api/v1/reports/portfolio");
}

/** Browser download link (BFF proxy, so the session cookie authenticates it) for the Portfolio PDF export. */
export function getPortfolioPdfHref() {
  return proxyHref("/reporting/portfolio/export.pdf");
}

// ---- Curated Portfolios: named, owned, scoped to a chosen subset of Spaces (see Portfolio.cs). ----

export async function listPortfolios() {
  return apiClient.get<Portfolio[]>("/api/v1/portfolios");
}

export async function createPortfolio(input: CreatePortfolioInput) {
  return apiClient.post<Portfolio>("/api/v1/portfolios", input);
}

export async function updatePortfolio(id: string, input: UpdatePortfolioInput) {
  return apiClient.patch<Portfolio>(`/api/v1/portfolios/${id}`, input);
}

export async function deletePortfolio(id: string) {
  await apiClient.delete<void>(`/api/v1/portfolios/${id}`);
}

/** The Health/Progress/Milestones/Risks/Budget rollup, scoped to just this portfolio's curated Spaces. */
export async function getPortfolioReport(id: string, params: { from?: string; to?: string } = {}) {
  const query = new URLSearchParams();
  if (params.from) query.append("from", params.from);
  if (params.to) query.append("to", params.to);
  const suffix = query.toString() ? `?${query.toString()}` : "";
  return apiClient.get<PortfolioRow[]>(`/api/v1/portfolios/${id}${suffix}`);
}

/**  . uc(d)rill-down from a Portfolio row's total/completed count to its (permission-filtered) task list. */
export async function getSpaceDrillDown(spaceId: string, completedOnly?: boolean) {
  const query = new URLSearchParams();
  if (completedOnly !== undefined) query.append("completedOnly", String(completedOnly));
  const suffix = query.toString() ? `?${query.toString()}` : "";
  return apiClient.get<DrillDownTask[]>(`/api/v1/reporting/drill-down/spaces/${spaceId}${suffix}`);
}

/**  . uc(d)rill-down from the dashboard's Overdue widget count to its (permission-filtered) task list. */
export async function getOverdueDrillDown() {
  return apiClient.get<DrillDownTask[]>("/api/v1/reporting/drill-down/overdue");
}

/** Drill-down from a Workload row to its assigned tasks. UNASSIGNED_USER_ID means the "Unassigned" bucket. */
export async function getAssigneeDrillDown(userId: string) {
  return apiClient.get<DrillDownTask[]>(`/api/v1/reporting/drill-down/assignee/${userId}`);
}

export async function listScheduledReports() {
  return apiClient.get<ScheduledReport[]>("/api/v1/reporting/scheduled-reports");
}

export async function createScheduledReport(input: CreateScheduledReportInput) {
  return apiClient.post<ScheduledReport>("/api/v1/reporting/scheduled-reports", input);
}

export async function setScheduledReportEnabled(id: string, enabled: boolean) {
  return apiClient.put<ScheduledReport>(`/api/v1/reporting/scheduled-reports/${id}/enabled`, { enabled });
}

export async function deleteScheduledReport(id: string) {
  await apiClient.delete<void>(`/api/v1/reporting/scheduled-reports/${id}`);
}
