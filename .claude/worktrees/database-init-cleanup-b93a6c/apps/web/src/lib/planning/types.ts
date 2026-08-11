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
};

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
  | "CustomFormula";

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
