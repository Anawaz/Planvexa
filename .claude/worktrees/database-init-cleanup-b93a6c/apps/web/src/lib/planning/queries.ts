import type {
  CalendarQuery,
  DashboardDataQuery,
  GanttQuery,
  WorkloadQuery,
} from "./types";

export const planningKeys = {
  all: ["planning"] as const,
  calendarRoot: () => [...planningKeys.all, "calendar"] as const,
  calendar: (params: CalendarQuery) => [...planningKeys.calendarRoot(), params] as const,
  ganttRoot: () => [...planningKeys.all, "gantt"] as const,
  gantt: (params: GanttQuery) => [...planningKeys.ganttRoot(), params] as const,
  workloadRoot: () => [...planningKeys.all, "workload"] as const,
  workload: (params: WorkloadQuery) => [...planningKeys.workloadRoot(), params] as const,
  teamRoot: () => [...planningKeys.all, "team"] as const,
  team: (params: WorkloadQuery) => [...planningKeys.teamRoot(), params] as const,
  workSchedule: () => [...planningKeys.all, "work-schedule"] as const,
  holidays: () => [...planningKeys.all, "holidays"] as const,
  leaveRoot: () => [...planningKeys.all, "leave"] as const,
  leave: (userId?: string) => [...planningKeys.leaveRoot(), userId ?? "all"] as const,
  sprints: () => [...planningKeys.all, "sprints"] as const,
  sprintBoard: (id: string) => [...planningKeys.sprints(), id, "board"] as const,
  dashboards: () => [...planningKeys.all, "dashboards"] as const,
  dashboard: (id: string) => [...planningKeys.dashboards(), id] as const,
  dashboardData: (id: string, params: DashboardDataQuery) =>
    [...planningKeys.dashboard(id), "data", params] as const,
  portfolio: () => [...planningKeys.all, "portfolio"] as const,
};
