export type StatusCategory = "NotStarted" | "Active" | "Done" | "Closed";

export type Priority = "None" | "Low" | "Normal" | "High" | "Urgent";

export type Space = {
  id: string;
  name: string;
  description?: string;
  color?: string;
  icon?: string;
  position: number;
  isArchived: boolean;
  isPrivate: boolean;
  defaultViewId?: string | null;
};

export type Folder = {
  id: string;
  spaceId: string;
  parentFolderId?: string | null;
  name: string;
  position: number;
  isPrivate: boolean;
  defaultViewId?: string | null;
};

export type TaskList = {
  id: string;
  spaceId: string;
  folderId?: string;
  name: string;
  description?: string;
  statusSchemeId?: string;
  position: number;
  isArchived?: boolean;
  isPrivate: boolean;
  defaultViewId?: string | null;
};

export type SavedViewType = "List" | "Table" | "Board" | "Calendar" | "Timeline" | "Gantt";

export type SavedView = {
  id: string;
  name: string;
  viewType: SavedViewType;
  scopeType: "Workspace" | "Space" | "Folder" | "List";
  scopeId?: string | null;
  configJson: string;
  isPrivate: boolean;
};

export type WorkTemplateResourceType = "Space" | "Folder" | "List";

export type WorkTemplate = {
  id: string;
  resourceType: WorkTemplateResourceType;
  name: string;
  createdAtUtc: string;
};

export type WorkFavorite = {
  id: string;
  resourceType: string;
  resourceId: string;
  createdAtUtc: string;
};

export type RecentItem = {
  resourceType: string;
  resourceId: string;
  viewedAtUtc: string;
};

export type StatusDefinition = {
  id: string;
  name: string;
  category: StatusCategory;
  color: string;
  position: number;
};

export type StatusScheme = {
  id: string;
  name: string;
  statuses: StatusDefinition[];
};

export type Task = {
  id: string;
  listId: string;
  spaceId: string;
  parentId?: string;
  sequence: string;
  title: string;
  description?: string;
  statusId: string;
  priority: Priority;
  startDate?: string;
  dueDate?: string;
  isMilestone: boolean;
  assigneeUserIds: string[];
  watcherUserIds: string[];
  tagIds: string[];
  position: number;
  isCompleted: boolean;
  isPrivate: boolean;
  /**  . uc(n)ull means "the workspace's built-in default type". */
  taskTypeId?: string | null;
  /**  . uc(o)ptional user-set id/key, unique per list, distinct from `sequence`. */
  customId?: string | null;
  /**  . uc(t)eams assigned to the task, alongside `assigneeUserIds` (individual users). */
  teamAssigneeIds: string[];
};

/**  . uc(a) workspace-configurable task type ("Task", "Bug", "Milestone", ...). */
export type TaskType = {
  id: string;
  name: string;
  color: string;
  icon?: string | null;
  isBuiltIn: boolean;
  position: number;
};

/**  . uc(o)ne of a task's List memberships (a task can belong to more than one List). */
export type TaskListMembershipInfo = {
  listId: string;
  isPrimary: boolean;
  position: number;
  addedAtUtc: string;
};

/**  . uc(a) free-form "relates to" link to another task. */
export type TaskRelationInfo = {
  relatedTaskId: string;
  createdAtUtc: string;
};

export type ChecklistItem = {
  id: string;
  title: string;
  isCompleted: boolean;
};

export type Checklist = {
  id: string;
  title: string;
  items: ChecklistItem[];
};

export type DependencyType = "BlockedBy" | "WaitingOn" | "Blocks";

export type TaskDependency = {
  id: string;
  dependsOnTaskId: string;
  type: DependencyType;
};

export type CustomFieldType =
  | "Text"
  | "LongText"
  | "Number"
  | "Currency"
  | "Boolean"
  | "Date"
  | "DateTime"
  | "Dropdown"
  | "MultiSelect"
  | "Url"
  | "Email"
  | "Rating"
  | "User"
  | "Team"
  | "Phone"
  | "Location"
  | "Progress"
  | "Formula"
  | "Relationship"
  | "Rollup";

export type CustomFieldOption = {
  id: string;
  label: string;
  color?: string | null;
  position: number;
};

export type CustomFieldRollupSourceType = "Subtasks" | "RelationshipField";
export type CustomFieldRollupFunction = "Sum" | "Count" | "Average" | "Min" | "Max";

export type CustomFieldDefinition = {
  id: string;
  name: string;
  type: CustomFieldType;
  scope: "Workspace" | "Space" | "Folder" | "List";
  scopeId?: string | null;
  isRequired: boolean;
  position: number;
  options: CustomFieldOption[];
  /** Formula fields only. */
  formulaExpression?: string | null;
  /** Rollup fields only. */
  rollupSourceType?: CustomFieldRollupSourceType | null;
  rollupSourceFieldId?: string | null;
  rollupTargetFieldId?: string | null;
  rollupFunction?: CustomFieldRollupFunction | null;
};

/** The typed projections the API stores/computes; only the slot matching the definition's type is set. */
export type CustomFieldValue = {
  definitionId: string;
  text?: string | null;
  number?: number | null;
  date?: string | null;
  boolean?: boolean | null;
  optionId?: string | null;
  /** User-type field. */
  userValue?: string | null;
  /** Team-type field. */
  teamValue?: string | null;
  /** Relationship-type field — linked task ids. */
  relatedTaskIds?: string[] | null;
  /** Set instead of a value when a Formula/Rollup field failed to evaluate. */
  computedError?: string | null;
};

export type ActivityEntry = {
  id: string;
  actorUserId: string;
  action: string;
  createdAt: string;
};

/**  . uc(o)ne row of the workspace-wide, permission-filtered activity feed (distinct from the
 * per-task ActivityEntry above). */
export type ActivityFeedItem = {
  id: string;
  taskId: string;
  taskTitle: string;
  actorUserId?: string | null;
  type: string;
  data?: string | null;
  createdAtUtc: string;
};

export type ActivityFeedQuery = {
  before?: string;
  take?: number;
  actorUserId?: string;
  from?: string;
  to?: string;
};

/**  . uc(a) task's Location-field value. */
export type LocationValue = {
  taskId: string;
  taskTitle: string;
  location: string;
};

/**  . uc(")if field X matches condition Y, apply style Z" -- stored per saved view in its configJson
 * (SavedView.configJson is already an opaque JSON blob, so no schema change was needed for this). */
export type ConditionalFormattingRule = {
  id: string;
  field: FilterFieldName;
  operator: FilterOperatorName;
  value?: string | null;
  color: string;
  style: "row" | "badge";
};

export type TaskDetail = Task & {
  checklists: Checklist[];
  dependencies: TaskDependency[];
  customFieldValues: CustomFieldValue[];
  activity: ActivityEntry[];
  lists: TaskListMembershipInfo[];
  relations: TaskRelationInfo[];
};

export type Tag = {
  id: string;
  name: string;
  color: string;
};

export type Reminder = {
  id: string;
  taskId: string;
  remindAtUtc: string;
  note?: string | null;
  isSent: boolean;
};

export type Attachment = {
  id: string;
  taskId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedByUserId: string;
  createdAtUtc: string;
};

export type ListTasksFilters = {
  status?: string;
  assignee?: string;
  tag?: string;
  groupBy?: "status";
  sort?: "position" | "title" | "priority" | "dueDate";
  /**  . uc(n)ested AND/OR filter groups, evaluated server-side via POST /lists/{id}/tasks/query.
   * When set, this is applied IN ADDITION to the flat status/assignee/tag filters above. */
  filterGroup?: FilterGroup;
};

export type FilterFieldName = "status" | "assignee" | "tag" | "priority" | "title" | "duedate" | "startdate" | "iscompleted";

export type FilterOperatorName =
  | "Equals"
  | "NotEquals"
  | "Contains"
  | "IsEmpty"
  | "IsNotEmpty"
  | "GreaterThan"
  | "LessThan"
  | "In";

export type FilterCondition = {
  field: FilterFieldName;
  operator: FilterOperatorName;
  value?: string | null;
};

/**  . uc(m)irrors the backend's FilterGroupDto -- Conditions are this node's own leaves, Groups are
 * child nodes combined with the same Logic. An empty group (no conditions, no groups) matches everything. */
export type FilterGroup = {
  logic: "And" | "Or";
  conditions?: FilterCondition[];
  groups?: FilterGroup[];
};

export type CreateTaskInput = {
  listId: string;
  title: string;
  description?: string;
  statusId?: string;
  priority?: Priority;
  startDate?: string;
  dueDate?: string;
  assigneeUserIds?: string[];
  watcherUserIds?: string[];
  tagIds?: string[];
  parentId?: string;
  isMilestone?: boolean;
  taskTypeId?: string;
  customId?: string;
};

export type UpdateTaskPatch = Partial<
  Pick<
    Task,
    | "title"
    | "description"
    | "priority"
    | "startDate"
    | "dueDate"
    | "assigneeUserIds"
    | "watcherUserIds"
    | "tagIds"
    | "parentId"
    | "isMilestone"
    | "isCompleted"
    | "position"
    | "taskTypeId"
    | "customId"
  >
> & {
  statusId?: string;
};

export type MoveTaskInput = {
  listId?: string;
  statusId?: string;
  position?: number;
};

/** Mirrors BulkTaskRequest — every field beyond `taskIds` is an optional operation. */
export type BulkTaskInput = {
  taskIds: string[];
  statusId?: string;
  addAssigneeUserId?: string;
  dueDate?: string;
};
