import { getFormatPreferences } from "@/lib/i18n/formatPreferences";

export type GoalTargetType = "Numeric" | "LinkedTasksRatio";
export type GoalStatus = "NotStarted" | "OnTrack" | "AtRisk" | "OffTrack" | "Completed" | "Archived";
export type GoalUnit = "Number" | "Currency" | "Percent";

/**
 * Formats a Numeric-target value (or a key result's) for display — purely cosmetic, does not affect
 * progress math. `locale` defaults to the signed-in user's saved preference, then the browser
 * default. Currency code is fixed at USD (no per-workspace currency selection exists yet) but the
 * symbol/grouping/decimal style still follow the resolved locale via Intl.NumberFormat.
 */
export function formatGoalValue(value: number, unit: GoalUnit, locale?: string): string {
  const resolvedLocale = locale ?? getFormatPreferences().locale;
  switch (unit) {
    case "Currency":
      return new Intl.NumberFormat(resolvedLocale, { style: "currency", currency: "USD" }).format(value);
    case "Percent":
      return `${value}%`;
    default:
      return value.toLocaleString(resolvedLocale);
  }
}

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
  unit: GoalUnit;
  status: GoalStatus;
  percentComplete: number;
  linkedTaskCount: number;
  completedLinkedTaskCount: number;
  keyResultCount: number;
};

export type GoalLinkedTask = {
  taskId: string;
  title: string | null;
  isCompleted: boolean | null;
  /** False when the viewer has no read access to this task — title/isCompleted are masked (null). */
  visible: boolean;
};

export type GoalKeyResult = {
  id: string;
  title: string;
  currentValue: number;
  targetValue: number;
  unit: GoalUnit;
  percentComplete: number;
};

export type GoalDetail = {
  goal: Goal;
  linkedTasks: GoalLinkedTask[];
  keyResults: GoalKeyResult[];
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
  unit?: GoalUnit;
};

export type LinkKeyResultInput = {
  title: string;
  targetValue: number;
  currentValue: number;
  unit: GoalUnit;
};

export type UpdateKeyResultInput = {
  title?: string;
  currentValue?: number;
  targetValue?: number;
  unit?: GoalUnit;
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
