export type GoalTargetType = "Numeric" | "LinkedTasksRatio";
export type GoalStatus = "NotStarted" | "OnTrack" | "AtRisk" | "OffTrack" | "Completed" | "Archived";

export type GoalFolder = {
  id: string;
  name: string;
};

export type Goal = {
  id: string;
  folderId: string | null;
  name: string;
  description: string | null;
  ownerUserId: string;
  startDate: string;
  endDate: string;
  targetType: GoalTargetType;
  targetValue: number | null;
  currentValue: number | null;
  status: GoalStatus;
  percentComplete: number;
  linkedTaskCount: number;
  completedLinkedTaskCount: number;
};

export type GoalLinkedTask = {
  taskId: string;
  title: string | null;
  isCompleted: boolean | null;
  /** False when the viewer has no read access to this task — title/isCompleted are masked (null). */
  visible: boolean;
};

export type GoalDetail = {
  goal: Goal;
  linkedTasks: GoalLinkedTask[];
};

export type GoalComment = {
  id: string;
  authorUserId: string;
  body: string;
  createdAtUtc: string;
};

export type CreateGoalInput = {
  folderId?: string;
  name: string;
  description?: string;
  ownerUserId?: string;
  startDate: string;
  endDate: string;
  targetType: GoalTargetType;
  targetValue?: number;
  currentValue?: number;
};

export type UpdateGoalInput = {
  name?: string;
  description?: string;
  folderId?: string;
  startDate?: string;
  endDate?: string;
  currentValue?: number;
  status?: GoalStatus;
};
