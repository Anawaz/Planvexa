import { apiClient, proxyHref, type ApiRequestOptions } from "../api-client";
import type {
  ActiveTimer,
  Budget,
  BudgetStatus,
  BudgetStatusParams,
  CreateBudgetInput,
  CreateTimeEntryInput,
  ListEntriesParams,
  MemberRate,
  ReportRow,
  StartTimerInput,
  TimeEntry,
  TimePolicy,
  TimeReportParams,
  TimeTag,
  TimesheetParams,
  TimesheetPeriod,
  UpdateBudgetInput,
  UpdateTimeEntryPatch,
  UtilizationParams,
  UtilizationRow,
} from "./types";

// API calls to backend

export async function startTimer(input: StartTimerInput = {}, options?: ApiRequestOptions) {
  const body = {
    taskId: input.taskId,
    description: input.description,
    isBillable: input.isBillable,
    tagIds: input.tagIds,
  };
  return apiClient.post<ActiveTimer>("/api/v1/timers/start", body, options);
}

export async function stopTimer(description?: string, options?: ApiRequestOptions) {
  return apiClient.post<TimeEntry>("/api/v1/timers/stop", { description }, options);
}

export async function pauseTimer() {
  return apiClient.post<ActiveTimer>("/api/v1/timers/pause", {});
}

export async function resumeTimer() {
  return apiClient.post<ActiveTimer>("/api/v1/timers/resume", {});
}

export async function getActiveTimer() {
  // The endpoint returns the entry itself when running, and `{ active: null }` when not.
  const response = await apiClient.get<ActiveTimer | { active: null }>("/api/v1/timers/active");
  return response && "id" in response ? response : null;
}

export async function listEntries(params: ListEntriesParams) {
  const query = new URLSearchParams();
  query.append("from", params.from);
  query.append("to", params.to);
  if (params.tagId) {
    query.append("tagId", params.tagId);
  }
  const path = `/api/v1/time-entries?${query.toString()}`;
  return apiClient.get<TimeEntry[]>(path);
}

export async function createEntry(input: CreateTimeEntryInput) {
  const body = {
    taskId: input.taskId,
    startedAtUtc: input.startedAtUtc,
    endedAtUtc: input.endedAtUtc,
    durationSeconds: input.durationSeconds,
    description: input.description,
    isBillable: input.isBillable,
    billingRate: input.billingRate,
    costRate: input.costRate,
    tagIds: input.tagIds,
  };
  return apiClient.post<TimeEntry>("/api/v1/time-entries", body);
}

export async function updateEntry(id: string, patch: UpdateTimeEntryPatch) {
  const body = {
    startedAtUtc: patch.startedAtUtc,
    endedAtUtc: patch.endedAtUtc,
    description: patch.description,
    isBillable: patch.isBillable,
    reason: patch.reason,
    tagIds: patch.tagIds,
  };
  return apiClient.patch<TimeEntry>(`/api/v1/time-entries/${id}`, body);
}

export async function deleteEntry(id: string) {
  await apiClient.delete<void>(`/api/v1/time-entries/${id}`);
}

export async function listTimeTags() {
  return apiClient.get<TimeTag[]>("/api/v1/time-tags");
}

export async function createTimeTag(name: string) {
  return apiClient.post<TimeTag>("/api/v1/time-tags", { name });
}

export async function getPolicy() {
  return apiClient.get<TimePolicy>("/api/v1/time-policy");
}

/** Admin-only (`TimeAuthorizer.EnsureManage`); the API replies 403 for Members and Guests. */
export async function updatePolicy(policy: TimePolicy) {
  return apiClient.put<TimePolicy>("/api/v1/time-policy", {
    singleActiveTimer: policy.singleActiveTimer,
    roundingMinutes: policy.roundingMinutes,
    minimumDurationSeconds: policy.minimumDurationSeconds,
    maximumEntrySeconds: policy.maximumEntrySeconds,
    billableByDefault: policy.billableByDefault,
    requireDescription: policy.requireDescription,
    requireTask: policy.requireTask,
    editWindowHours: policy.editWindowHours,
    approvalRequired: policy.approvalRequired,
    weekStartsOn: policy.weekStartsOn,
    lockDateUtc: policy.lockDateUtc ?? null,
    overtimeThresholdSeconds: policy.overtimeThresholdSeconds,
    missingTimeReminderEnabled: policy.missingTimeReminderEnabled,
    missingTimeReminderCadence: policy.missingTimeReminderCadence,
    missingTimeReminderMinimumSeconds: policy.missingTimeReminderMinimumSeconds,
  });
}

/** Admin-only. Workspace-default rates only; per-project overrides are not exposed by the API. */
export async function listRates() {
  return apiClient.get<MemberRate[]>("/api/v1/rates");
}

/** Admin-only. */
export async function setUserRate(userId: string, rate: { billingRate: number; costRate: number }) {
  return apiClient.put<MemberRate>(`/api/v1/rates/user/${userId}`, rate);
}

export async function getTimesheet(params: TimesheetParams) {
  const query = new URLSearchParams({ weekStart: params.weekStart });
  if (params.tagId) {
    query.append("tagId", params.tagId);
  }
  return apiClient.get<TimesheetPeriod>(`/api/v1/timesheets?${query.toString()}`);
}

export async function submitTimesheet(weekStart: string) {
  const body = { weekStartUtc: weekStart };
  return apiClient.post<TimesheetPeriod>("/api/v1/timesheets/submit", body);
}

export async function approveTimesheet(id: string) {
  return apiClient.post<TimesheetPeriod>(`/api/v1/timesheets/${id}/approve`, {});
}

export async function rejectTimesheet(id: string, comment: string) {
  const body = { comment };
  return apiClient.post<TimesheetPeriod>(`/api/v1/timesheets/${id}/reject`, body);
}

export async function reopenTimesheet(id: string) {
  return apiClient.post<TimesheetPeriod>(`/api/v1/timesheets/${id}/reopen`, {});
}

export async function getTimeReport(params: TimeReportParams) {
  const query = new URLSearchParams();
  query.append("groupBy", params.groupBy);
  query.append("from", params.from);
  query.append("to", params.to);
  if (params.tagId) {
    query.append("tagId", params.tagId);
  }
  const path = `/api/v1/reports/time?${query.toString()}`;
  return apiClient.get<ReportRow[]>(path);
}

/** Admin-only. */
export async function getUtilizationReport(params: UtilizationParams) {
  const query = new URLSearchParams({ from: params.from, to: params.to });
  return apiClient.get<UtilizationRow[]>(`/api/v1/reports/utilization?${query.toString()}`);
}

/** Browser download link (BFF proxy, so the session cookie authenticates it) for the accounting-system CSV export. Admin-only on the API. */
export function accountingExportHref(params: TimeReportParams) {
  return proxyHref("/reports/time/export/accounting", { from: params.from, to: params.to, tagId: params.tagId });
}

// ---- Budgets (Admin-only: TimeAuthorizer.EnsureManage) ----

export async function listBudgets() {
  return apiClient.get<Budget[]>("/api/v1/budgets");
}

export async function createBudget(input: CreateBudgetInput) {
  return apiClient.post<Budget>("/api/v1/budgets", input);
}

export async function updateBudget(id: string, input: UpdateBudgetInput) {
  return apiClient.put<Budget>(`/api/v1/budgets/${id}`, input);
}

export async function deleteBudget(id: string) {
  await apiClient.delete<void>(`/api/v1/budgets/${id}`);
}

export async function getBudgetStatus(id: string, params: BudgetStatusParams) {
  const query = new URLSearchParams({ from: params.from, to: params.to });
  return apiClient.get<BudgetStatus>(`/api/v1/budgets/${id}/status?${query.toString()}`);
}
