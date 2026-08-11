export type TimeEntrySource = "Timer" | "Manual";

export type TimeApprovalStatus =
  | "Draft"
  | "Submitted"
  | "Approved"
  | "Rejected"
  | "Locked";

export type TimeTag = {
  id: string;
  name: string;
};

export type TimeEntry = {
  id: string;
  userId: string;
  taskId?: string | null;
  startedAtUtc: string;
  endedAtUtc?: string | null;
  durationSeconds: number;
  timeZoneId: string;
  description?: string;
  isBillable: boolean;
  billingRate: number;
  costRate: number;
  source: TimeEntrySource;
  approvalStatus: TimeApprovalStatus;
  tags: TimeTag[];
  isPaused: boolean;
  pausedAtUtc?: string | null;
  pausedSeconds: number;
};

export type MissingTimeReminderCadence = "Daily" | "Weekly";

export type TimePolicy = {
  singleActiveTimer: boolean;
  roundingMinutes: number;
  minimumDurationSeconds: number;
  maximumEntrySeconds: number;
  billableByDefault: boolean;
  requireDescription: boolean;
  requireTask: boolean;
  editWindowHours: number;
  approvalRequired: boolean;
  weekStartsOn: number;
  lockDateUtc?: string | null;
  overtimeThresholdSeconds: number;
  missingTimeReminderEnabled: boolean;
  missingTimeReminderCadence: MissingTimeReminderCadence;
  missingTimeReminderMinimumSeconds: number;
};

export type BudgetScopeType = "Space" | "List";

export type Budget = {
  id: string;
  name: string;
  scopeType: BudgetScopeType;
  scopeId: string;
  monetaryCapAmount?: number | null;
  timeCapSeconds?: number | null;
};

export type BudgetStatus = {
  budgetId: string;
  name: string;
  scopeType: BudgetScopeType;
  scopeId: string;
  monetaryCapAmount?: number | null;
  timeCapSeconds?: number | null;
  hours: number;
  cost: number;
  revenue: number;
  profit: number;
  monetaryConsumedPercent?: number | null;
  timeConsumedPercent?: number | null;
};

export type TimesheetPeriod = {
  id: string;
  userId: string;
  periodStartUtc: string;
  periodEndUtc: string;
  status: TimeApprovalStatus;
  totalSeconds: number;
  billableSeconds: number;
  revenue: number;
  cost: number;
  entries: TimeEntry[];
};

export type ReportRow = {
  key: string;
  label: string;
  hours: number;
  billableHours: number;
  cost: number;
  revenue: number;
};

export type ActiveTimer = TimeEntry & {
  taskTitle?: string;
};

export type StartTimerInput = {
  taskId?: string | null;
  description?: string;
  isBillable?: boolean;
  tagIds?: string[];
};

export type CreateTimeEntryInput = {
  taskId?: string | null;
  startedAtUtc: string;
  endedAtUtc?: string | null;
  durationSeconds?: number;
  description?: string;
  isBillable?: boolean;
  billingRate?: number;
  costRate?: number;
  tagIds?: string[];
};

/**
 * Mirrors `UpdateTimeEntryRequest` on the API: a partial patch where the duration is derived from
 * start/end server-side, and rates/task are owned by the move + rate endpoints instead. `tagIds`
 * omitted leaves the entry's tags unchanged; an (empty or non-empty) array replaces the full set.
 */
export type UpdateTimeEntryPatch = {
  startedAtUtc?: string;
  endedAtUtc?: string | null;
  description?: string;
  isBillable?: boolean;
  reason?: string;
  tagIds?: string[];
};

export type MemberRate = {
  userId: string;
  billingRate: number;
  costRate: number;
};

export type UtilizationRow = {
  userId: string;
  trackedHours: number;
  billableHours: number;
  utilizationPercent: number;
};

export type ListEntriesParams = {
  from: string;
  to: string;
  tagId?: string;
};

export type TimesheetParams = {
  weekStart: string;
  tagId?: string;
};

export type TimeReportGroupBy = "project" | "task" | "user";

export type TimeReportParams = {
  groupBy: TimeReportGroupBy;
  tagId?: string;
  from: string;
  to: string;
};

export type UtilizationParams = {
  from: string;
  to: string;
};

export type CreateBudgetInput = {
  scopeType: BudgetScopeType;
  scopeId: string;
  name: string;
  monetaryCapAmount?: number | null;
  timeCapSeconds?: number | null;
};

export type UpdateBudgetInput = {
  name: string;
  monetaryCapAmount?: number | null;
  timeCapSeconds?: number | null;
};

export type BudgetStatusParams = {
  from: string;
  to: string;
};
