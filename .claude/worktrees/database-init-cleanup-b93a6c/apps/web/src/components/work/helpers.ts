import type {
  ConditionalFormattingRule,
  CustomFieldDefinition,
  CustomFieldType,
  CustomFieldValue,
  Priority,
  StatusDefinition,
  Task,
} from "@/lib/work/types";
import { cn } from "@/lib/utils";

/**  . uc(m)irrors the field set/semantics of the backend's TaskFilterEvaluator (WorkManagement), kept
 * intentionally simple (single condition, no groups) since conditional formatting is one rule at a
 * time, not a nested filter tree. */
export function ruleMatchesTask(task: Task, rule: Pick<ConditionalFormattingRule, "field" | "operator" | "value">) {
  const { field, operator, value } = rule;

  switch (field) {
    case "status":
      return operator === "NotEquals" ? task.statusId !== value : task.statusId === value;
    case "assignee":
      return operator === "NotEquals"
        ? !task.assigneeUserIds.includes(value ?? "")
        : task.assigneeUserIds.includes(value ?? "");
    case "tag":
      return operator === "NotEquals" ? !task.tagIds.includes(value ?? "") : task.tagIds.includes(value ?? "");
    case "priority": {
      const values = (value ?? "").split(",").map((v) => v.trim());
      const matches = values.includes(task.priority);
      return operator === "NotEquals" ? !matches : matches;
    }
    case "title":
      if (operator === "Contains") return task.title.toLowerCase().includes((value ?? "").toLowerCase());
      return operator === "NotEquals" ? task.title !== value : task.title === value;
    case "iscompleted":
      return task.isCompleted === (value !== "false");
    case "duedate":
    case "startdate": {
      const taskValue = field === "duedate" ? task.dueDate : task.startDate;
      if (operator === "IsEmpty") return !taskValue;
      if (operator === "IsNotEmpty") return Boolean(taskValue);
      if (!taskValue || !value) return false;
      if (operator === "GreaterThan") return taskValue > value;
      if (operator === "LessThan") return taskValue < value;
      return operator === "NotEquals" ? taskValue !== value : taskValue.slice(0, 10) === value.slice(0, 10);
    }
    default:
      return false;
  }
}

/** First matching rule (rules apply in list order, like CSS specificity by declaration order). */
export function firstMatchingRule(task: Task, rules: ConditionalFormattingRule[]) {
  return rules.find((rule) => ruleMatchesTask(task, rule));
}

const priorityTone: Record<Priority, string> = {
  None: "bg-muted text-muted-foreground",
  Low: "bg-slate-100 text-slate-700 dark:bg-slate-900 dark:text-slate-200",
  Normal: "bg-blue-100 text-blue-700 dark:bg-blue-950 dark:text-blue-200",
  High: "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200",
  Urgent: "bg-red-100 text-red-700 dark:bg-red-950 dark:text-red-200",
};

export function priorityClassName(priority: Priority) {
  return cn("rounded-full px-2 py-0.5 text-xs font-medium", priorityTone[priority]);
}

export function findStatus(statuses: StatusDefinition[], statusId: string) {
  return statuses.find((status) => status.id === statusId);
}

/**
 * The status colour carries the border and a 9% background tint only. Using it as the text colour
 * too failed axe's colour-contrast rule on every status (amber was 2:1 against its own tint); the
 * label inherits the theme foreground instead, which is contrast-checked in both themes.
 */
export function statusBadgeStyle(status?: StatusDefinition) {
  const color = status?.color ?? "#64748b";
  return {
    borderColor: `${color}55`,
    backgroundColor: `${color}16`,
  };
}

export function formatDate(date?: string) {
  if (!date) {
    return "No date";
  }

  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
  }).format(new Date(`${date}T12:00:00`));
}

export function dueDateClassName(date?: string, isCompleted = false) {
  if (!date || isCompleted) {
    return "text-muted-foreground";
  }

  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const due = new Date(`${date}T00:00:00`);
  const diffDays = Math.ceil((due.getTime() - today.getTime()) / 86_400_000);

  if (diffDays < 0) {
    return "text-red-600 dark:text-red-400";
  }

  if (diffDays <= 2) {
    return "text-amber-700 dark:text-amber-300";
  }

  return "text-muted-foreground";
}

export function sortStatuses(statuses: StatusDefinition[]) {
  return [...statuses].sort((left, right) => left.position - right.position);
}

export function groupTasksByStatus(tasks: Task[], statuses: StatusDefinition[]) {
  return sortStatuses(statuses).map((status) => ({
    status,
    tasks: tasks
      .filter((task) => task.statusId === status.id)
      .sort((left, right) => left.position - right.position),
  }));
}

/**
 * Splits a flat task array into top-level rows and a parent -> children index. A task whose parent
 * is filtered out of the current view is promoted to a root so it never disappears.
 */
export function buildTaskTree(tasks: Task[]) {
  const visibleIds = new Set(tasks.map((task) => task.id));
  const childrenOf = new Map<string, Task[]>();

  tasks.forEach((task) => {
    if (task.parentId && visibleIds.has(task.parentId)) {
      childrenOf.set(task.parentId, [...(childrenOf.get(task.parentId) ?? []), task]);
    }
  });

  return {
    childrenOf,
    roots: tasks.filter((task) => !task.parentId || !visibleIds.has(task.parentId)),
  };
}

/** Readable stand-in for a task we cannot resolve a title for (e.g. a dependency in another list). */
export function shortId(id: string) {
  return `#${id.slice(0, 8)}`;
}

/** Which control a custom field gets. Everything unhandled stays read-only rather than guessing. */
export function customFieldEditor(
  type: CustomFieldType,
): "text" | "number" | "date" | "boolean" | "dropdown" | "user" | "team" | "relationship" | "computed" | "readonly" {
  switch (type) {
    case "Text":
    case "LongText":
    case "Url":
    case "Email":
    // Phone/Location are free text — basic format validation happens server-side.
    case "Phone":
    case "Location":
      return "text";
    case "Number":
    case "Currency":
    case "Rating":
    // A 0-100 numeric percentage — the "number" input's min/max bounds it (see TaskDetailPanel).
    case "Progress":
      return "number";
    case "Date":
    case "DateTime":
      return "date";
    case "Boolean":
      return "boolean";
    case "Dropdown":
      return "dropdown";
    case "User":
      return "user";
    case "Team":
      return "team";
    case "Relationship":
      return "relationship";
    case "Formula":
    case "Rollup":
      return "computed";
    default:
      return "readonly";
  }
}

/** The stored value as the string its editor expects (and the API parses back). */
export function customFieldInputValue(
  definition: CustomFieldDefinition,
  value?: CustomFieldValue,
): string {
  if (!value) {
    return "";
  }

  switch (customFieldEditor(definition.type)) {
    case "number":
      return value.number == null ? "" : String(value.number);
    case "date":
      return value.date ?? "";
    case "boolean":
      return value.boolean == null ? "" : String(value.boolean);
    case "dropdown":
      return value.optionId ?? "";
    case "user":
      return value.userValue ?? "";
    case "team":
      return value.teamValue ?? "";
    case "computed":
      if (value.computedError) {
        return value.computedError;
      }
      return value.number == null ? "" : String(value.number);
    default:
      return value.text ?? "";
  }
}

export function tagLabel(tagId: string) {
  return tagId
    .replace(/^tag-/, "")
    .replaceAll("-", " ")
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}
