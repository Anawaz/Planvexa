export type CalendarTask = {
  id: string;
  title: string;
  dueDate: string;
  isCompleted: boolean;
  priority: string;
  assigneeUserIds: string[];
};

export type GanttBar = {
  id: string;
  title: string;
  startDate?: string | null;
  dueDate?: string | null;
  isMilestone: boolean;
  progress: number;
  dependsOn: string[];
  assigneeUserIds: string[];
  /**  . uc(o)n the longest (zero-slack) dependency chain, per standard CPM. */
  isCritical: boolean;
  /**  . uc(l)ast-captured "planned" snapshot of start/due date, null until set via setTaskBaseline. */
  baselineStartDate?: string | null;
  baselineDueDate?: string | null;
};

export type WorkloadRow = {
  userId: string;
  capacityHours: number;
  scheduledHours: number;
  loggedHours: number;
  isOverAllocated: boolean;
};

export type WorkSchedule = {
  workingDays: number[];
  dailyCapacityHours: number;
};

export type Holiday = {
  id: string;
  dateUtc: string;
  name: string;
};

export type LeaveEntry = {
  id: string;
  userId: string;
  startUtc: string;
  endUtc: string;
  type: string;
};

export type Sprint = {
  id: string;
  name: string;
  startUtc: string;
  endUtc: string;
  status: string;
  totalPoints: number;
  goal?: string | null;
};

export type SprintStatus = "Planned" | "Active" | "Completed";

export type SprintBoardTask = {
  id: string;
  title: string;
  points?: number;
};

export type SprintBoardColumn = {
  statusId: string;
  statusName: string;
  tasks: SprintBoardTask[];
};

export type DashboardWidgetType =
  | "TasksByStatus"
  | "Overdue"
  | "Completed"
  | "TimeLogged"
  | "BillableTotals"
  | "Workload"
  | "EstimateVsActual"
  | "SprintProgress"
  | "PortfolioHealth"
  | "Burndown"
  | "CustomFormula"
  | "Velocity"
  | "TasksByAssignee"
  | "TasksByPriority"
  | "CreatedVsCompleted"
  | "GoalProgress"
  | "CustomFieldBreakdown";

export type DashboardWidget = {
  id: string;
  type: DashboardWidgetType;
  config: Record<string, unknown>;
};

export type Dashboard = {
  id: string;
  name: string;
  isPrivate: boolean;
  widgets: DashboardWidget[];
};

// GET /dashboards (list) returns DashboardSummaryDto -- a widget count, not the full widgets array
// (that only comes back from GET /dashboards/{id}).
export type DashboardSummary = {
  id: string;
  name: string;
  isPrivate: boolean;
  widgetCount: number;
};

export type WidgetData = {
  widgetId: string;
  type: string;
  series: { label: string; value: number }[];
};

export type Milestone = {
  taskId: string;
  title: string;
  dueDate: string | null;
  isCompleted: boolean;
};

export type Risk = {
  id: string;
  title: string;
  description: string | null;
  severity: "Low" | "Medium" | "High" | "Critical";
  scopeType: "Space" | "List" | "Goal";
  scopeId: string;
  status: "Open" | "Mitigating" | "Resolved" | "Accepted";
};

export type BudgetStatus = {
  monetaryCapAmount: number | null;
  timeCapSeconds: number | null;
  hours: number;
  cost: number;
  monetaryConsumedPercent: number | null;
  timeConsumedPercent: number | null;
};

export type PortfolioRow = {
  key: string;
  label: string;
  totalTasks: number;
  completedTasks: number;
  loggedHours: number;
  healthPercent: number;
  milestones: Milestone[];
  risks: Risk[];
  budget: BudgetStatus | null;
};

export type PortfolioStatus = "OnTrack" | "AtRisk" | "OffTrack";

// A named, owned, curated Portfolio (chosen subset of Spaces) -- distinct from PortfolioRow above,
// which is one row of a computed rollup (workspace-wide or scoped to a Portfolio's SpaceIds).
export type Portfolio = {
  id: string;
  name: string;
  ownerUserId: string;
  isPrivate: boolean;
  status: PortfolioStatus;
  startUtc: string | null;
  targetEndUtc: string | null;
  spaceIds: string[];
};

export type CreatePortfolioInput = {
  name: string;
  ownerUserId?: string | null;
  isPrivate: boolean;
  status: PortfolioStatus;
  startUtc?: string | null;
  targetEndUtc?: string | null;
  spaceIds: string[];
};

export type UpdatePortfolioInput = Partial<CreatePortfolioInput>;

export type DrillDownTask = {
  taskId: string;
  title: string;
  statusName: string;
  isCompleted: boolean;
};

export type CalendarQuery = {
  from: string;
  to: string;
  scopeId?: string;
};

export type GanttQuery = {
  spaceId: string;
};

export type WorkloadQuery = {
  from: string;
  to: string;
};

/**  . uc(T)eam view -- the same capacity/scheduled/logged shape as WorkloadRow, grouped by Team. */
export type TeamWorkloadMember = {
  userId: string;
  capacityHours: number;
  scheduledHours: number;
  loggedHours: number;
  isOverAllocated: boolean;
};

export type TeamWorkloadRow = {
  teamId: string;
  teamName: string;
  capacityHours: number;
  scheduledHours: number;
  loggedHours: number;
  members: TeamWorkloadMember[];
};

export type DashboardDataQuery = {
  from: string;
  to: string;
};

export type CreateHolidayInput = {
  dateUtc: string;
  name: string;
};

export type CreateLeaveInput = {
  userId: string;
  startUtc: string;
  endUtc: string;
  type: string;
};

export type CreateSprintInput = {
  name: string;
  startUtc: string;
  endUtc: string;
  goal?: string;
};

export type UpdateSprintInput = {
  name?: string;
  startUtc?: string;
  endUtc?: string;
  goal?: string;
};

export type AddSprintItemInput = {
  taskId: string;
  points?: number;
};

export type CreateDashboardInput = {
  name: string;
  isPrivate: boolean;
  widgets: Omit<DashboardWidget, "id">[];
};

export type UpdateDashboardInput = {
  name?: string;
  isPrivate?: boolean;
  widgets?: Omit<DashboardWidget, "id">[];
};

export type ScheduledReportCadence = "Daily" | "Weekly";

// Recipients are raw email addresses (ScheduledReport.cs), not necessarily workspace members.
export type ScheduledReport = {
  id: string;
  dashboardId: string;
  recipients: string[];
  cadence: ScheduledReportCadence;
  isEnabled: boolean;
  lastSentAtUtc: string | null;
};

export type CreateScheduledReportInput = {
  dashboardId: string;
  recipients: string[];
  cadence: ScheduledReportCadence;
};
