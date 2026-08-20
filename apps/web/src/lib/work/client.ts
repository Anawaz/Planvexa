import { apiClient, ApiError, getApiContext, proxyHref, type ApiRequestOptions } from "../api-client";
import { isOnline } from "../offline/connectivity";
import { cacheGetAll, cachePut, cachePutMany } from "../offline/db";
import type {
  ActivityFeedItem,
  ActivityFeedQuery,
  Attachment,
  BulkTaskInput,
  CreateTaskInput,
  CustomFieldDefinition,
  CustomFieldValue,
  DependencyType,
  Folder,
  ListTasksFilters,
  LocationValue,
  MoveTaskInput,
  Priority,
  RecentItem,
  Reminder,
  SavedView,
  Space,
  SpaceStatusScheme,
  StatusCategory,
  StatusScheme,
  Tag,
  Task,
  TaskDependency,
  TaskDetail,
  TaskList,
  TaskListMembershipInfo,
  TaskRelationInfo,
  MyWorkPreferences,
  TaskType,
  UpdateTaskPatch,
  WorkFavorite,
  WorkTemplate,
  WorkTemplateResourceType,
} from "./types";

// ---- Backend wire shapes (WorkManagement Contracts.cs) ----

type TaskDto = {
  id: string;
  listId: string;
  spaceId: string;
  parentId?: string | null;
  sequence: number;
  title: string;
  description?: string | null;
  statusId: string;
  priority: Priority;
  startDate?: string | null;
  dueDate?: string | null;
  isMilestone: boolean;
  isCompleted: boolean;
  position: number;
  assigneeUserIds: string[];
  tagIds: string[];
  isPrivate: boolean;
  taskTypeId?: string | null;
  customId?: string | null;
  teamAssigneeIds: string[];
  isArchived: boolean;
  createdByUserId?: string | null;
};

type TaskDetailDto = {
  task: TaskDto;
  watcherUserIds: string[];
  checklists: Array<{
    id: string;
    name: string;
    position: number;
    items: Array<{ id: string; content: string; isResolved: boolean; position: number }>;
  }>;
  dependencies: Array<{ id: string; dependsOnTaskId: string; type: DependencyType }>;
  customFieldValues: Array<{
    definitionId: string;
    text?: string | null;
    number?: number | null;
    date?: string | null;
    boolean?: boolean | null;
    optionId?: string | null;
  }>;
  activity: Array<{
    id: string;
    actorUserId?: string | null;
    type: string;
    data?: string | null;
    createdAtUtc: string;
  }>;
  lists: TaskListMembershipInfo[];
  relations: TaskRelationInfo[];
};

function mapTask(dto: TaskDto): Task {
  return {
    id: dto.id,
    listId: dto.listId,
    spaceId: dto.spaceId,
    parentId: dto.parentId ?? undefined,
    sequence: String(dto.sequence),
    title: dto.title,
    description: dto.description ?? undefined,
    statusId: dto.statusId,
    priority: dto.priority,
    startDate: dto.startDate?.slice(0, 10),
    dueDate: dto.dueDate?.slice(0, 10),
    isMilestone: dto.isMilestone,
    isCompleted: dto.isCompleted,
    position: dto.position,
    assigneeUserIds: dto.assigneeUserIds,
    tagIds: dto.tagIds,
    isPrivate: dto.isPrivate,
    taskTypeId: dto.taskTypeId ?? undefined,
    customId: dto.customId ?? undefined,
    teamAssigneeIds: dto.teamAssigneeIds,
    isArchived: dto.isArchived,
    createdByUserId: dto.createdByUserId ?? undefined,
    // List rows do not carry watchers; only the detail endpoint returns them.
    watcherUserIds: [],
  };
}

function mapTaskDetail(dto: TaskDetailDto): TaskDetail {
  return {
    ...mapTask(dto.task),
    watcherUserIds: dto.watcherUserIds,
    checklists: dto.checklists.map((checklist) => ({
      id: checklist.id,
      title: checklist.name,
      items: checklist.items.map((item) => ({
        id: item.id,
        title: item.content,
        isCompleted: item.isResolved,
      })),
    })),
    // Titles/labels are joined by the caller: dependency titles come from the list's task query and
    // custom-field names from `listCustomFields()`, both of which react-query already caches.
    dependencies: dto.dependencies,
    customFieldValues: dto.customFieldValues.map((value) => ({
      ...value,
      date: value.date?.slice(0, 10) ?? null,
    })),
    activity: dto.activity.map((entry) => ({
      id: entry.id,
      actorUserId: entry.actorUserId ?? "system",
      action: entry.type,
      createdAt: entry.createdAtUtc,
    })),
    lists: dto.lists,
    relations: dto.relations,
  };
}

/**
 * Dates travel as `YYYY-MM-DD` through the UI, but the API binds them to `DateTimeOffset` and
 * Postgres only accepts a UTC offset — a bare date picks up the server's local offset and the write
 * fails with a 500. Pin date-only values to midnight UTC; anything already carrying a time is left
 * alone. Applied at every write that sends a date (create, update, bulk).
 */
export function toUtcInstant(date?: string) {
  if (!date) {
    return date;
  }

  return /^\d{4}-\d{2}-\d{2}$/.test(date) ? `${date}T00:00:00Z` : date;
}

const priorityWeight: Record<Priority, number> = {
  None: 0,
  Low: 1,
  Normal: 2,
  High: 3,
  Urgent: 4,
};

const comparators: Record<NonNullable<ListTasksFilters["sort"]>, (a: Task, b: Task) => number> = {
  position: (a, b) => a.position - b.position,
  title: (a, b) => a.title.localeCompare(b.title),
  priority: (a, b) => priorityWeight[b.priority] - priorityWeight[a.priority],
  dueDate: (a, b) => (a.dueDate ?? "9999-99-99").localeCompare(b.dueDate ?? "9999-99-99"),
};

// API calls to backend

export async function listSpaces() {
  return apiClient.get<Space[]>("/api/v1/spaces");
}

export async function listFolders(spaceId: string) {
  return apiClient.get<Folder[]>(`/api/v1/spaces/${spaceId}/folders`);
}

export async function listLists(spaceId: string) {
  return apiClient.get<TaskList[]>(`/api/v1/spaces/${spaceId}/lists`);
}

export async function getList(listId: string) {
  return apiClient.get<TaskList>(`/api/v1/lists/${listId}`);
}

/** All schemes for the workspace; callers pick by the list's statusSchemeId. */
export async function listStatusSchemes() {
  return apiClient.get<StatusScheme[]>("/api/v1/status-schemes");
}

/** Only the workspace-level schemes — per-Space overrides are excluded, so the workspace status
 * settings page shows the defaults and nothing else. Kept separate from `listStatusSchemes` because
 * that one is passed straight to `queryFn`, which would supply the query context as an argument. */
export async function listWorkspaceStatusSchemes() {
  return apiClient.get<StatusScheme[]>("/api/v1/status-schemes?workspaceLevelOnly=true");
}

export type StatusInput = { name: string; category: StatusCategory; color?: string };

export async function createStatusScheme(name: string, statuses: StatusInput[]) {
  return apiClient.post<StatusScheme>("/api/v1/status-schemes", { name, statuses });
}

export async function renameStatusScheme(schemeId: string, name: string) {
  return apiClient.patch<StatusScheme>(`/api/v1/status-schemes/${schemeId}`, { name });
}

export async function deleteStatusScheme(schemeId: string) {
  return apiClient.delete<void>(`/api/v1/status-schemes/${schemeId}`);
}

export async function addStatus(schemeId: string, status: StatusInput) {
  return apiClient.post<StatusScheme>(`/api/v1/status-schemes/${schemeId}/statuses`, status);
}

/** Every field is optional (null = unchanged); `index` reorders 0-based after the edit. */
export async function updateStatus(
  schemeId: string,
  statusId: string,
  patch: { name?: string; category?: StatusCategory; color?: string; index?: number },
) {
  return apiClient.patch<StatusScheme>(`/api/v1/status-schemes/${schemeId}/statuses/${statusId}`, patch);
}

/** A status is never removed out from under its tasks — the replacement is required, not optional. */
export async function removeStatus(schemeId: string, statusId: string, moveTasksToStatusId: string) {
  return apiClient.delete<StatusScheme>(`/api/v1/status-schemes/${schemeId}/statuses/${statusId}`, {
    moveTasksToStatusId,
  });
}

export async function getSpaceStatusScheme(spaceId: string) {
  return apiClient.get<SpaceStatusScheme>(`/api/v1/spaces/${spaceId}/status-scheme`);
}

/** No preset clones the current effective scheme losslessly; a preset moves every task in the Space
 * onto the new scheme's default status. Idempotent on an already-customized Space. */
export async function customizeSpaceStatusScheme(spaceId: string, presetStatuses?: StatusInput[]) {
  return apiClient.post<SpaceStatusScheme>(`/api/v1/spaces/${spaceId}/status-scheme`, {
    presetStatuses: presetStatuses ?? null,
  });
}

/** Reverts to the workspace default; every Space status that still holds tasks needs a mapping entry. */
export async function resetSpaceStatusScheme(
  spaceId: string,
  mapping: { fromStatusId: string; toStatusId: string }[],
) {
  return apiClient.delete<SpaceStatusScheme>(`/api/v1/spaces/${spaceId}/status-scheme`, { mapping });
}

/** Configures the optional allowed-transitions restriction for one status; an empty list clears it
 * (unrestricted again). Enforced by the backend on every status change. */
export async function setStatusTransitions(schemeId: string, statusId: string, toStatusIds: string[]) {
  return apiClient.put<StatusScheme>(`/api/v1/status-schemes/${schemeId}/statuses/${statusId}/transitions`, {
    toStatusIds,
  });
}

export async function listTasks(listId: string, filters: ListTasksFilters = {}) {
  const workspaceId = getApiContext().workspaceId;

  try {
    // A nested filter group is evaluated server-side (WorkItemService.QueryByListAsync /
    // TaskFilterEvaluator) -- POST because the tree doesn't fit a query string. Otherwise fall back to
    // the plain GET + flat client-side filter below.
    const dtos = filters.filterGroup
      ? await apiClient.post<TaskDto[]>(`/api/v1/lists/${listId}/tasks/query`, filters.filterGroup)
      : await apiClient.get<TaskDto[]>(`/api/v1/lists/${listId}/tasks`);

    const tasks = dtos.map(mapTask);
    // Write-through to the workspace-scoped IndexedDB cache for offline reading -- best
    // effort, never blocks or fails the live response.
    if (workspaceId) {
      void cachePutMany(workspaceId, "task", tasks.map((task) => ({ id: task.id, data: task })));
    }

    // ponytail: client-side filtering for the flat status/assignee/tag filters; push to SQL past ~1k
    // tasks/list. (Nested filter groups already go through the server -- see above.)
    return tasks
      .filter(
        (task) =>
          (!filters.status || task.statusId === filters.status) &&
          (!filters.assignee || task.assigneeUserIds.includes(filters.assignee)) &&
          (!filters.tag || task.tagIds.includes(filters.tag)),
      )
      .sort(comparators[filters.sort ?? "position"]);
  } catch (error) {
    // Offline reading of already-visited data: fall back to the last cached
    // snapshot for this list rather than surfacing an error. A real ApiError (server reachable, e.g.
    // 403/404) is NOT swallowed -- only a genuine network failure while offline is.
    if (workspaceId && !isOnline() && !(error instanceof ApiError)) {
      const cached = await cacheGetAll(workspaceId, "task");
      return cached
        .map((entry) => entry.data as Task)
        .filter((task) => task.listId === listId)
        .sort(comparators[filters.sort ?? "position"]);
    }
    throw error;
  }
}

/**  . uc(w)orkspace-wide, permission-filtered activity feed. */
export async function getWorkspaceActivity(query: ActivityFeedQuery = {}) {
  const params = new URLSearchParams();
  if (query.before) params.append("before", query.before);
  if (query.take) params.append("take", String(query.take));
  if (query.actorUserId) params.append("actorUserId", query.actorUserId);
  if (query.from) params.append("from", query.from);
  if (query.to) params.append("to", query.to);
  const suffix = params.toString() ? `?${params.toString()}` : "";
  return apiClient.get<ActivityFeedItem[]>(`/api/v1/activity${suffix}`);
}

/**  . uc(t)asks in a list with a value for a Location-type custom field. */
export async function listLocationValues(listId: string, definitionId: string) {
  return apiClient.get<LocationValue[]>(`/api/v1/lists/${listId}/custom-fields/${definitionId}/locations`);
}

export async function getTask(id: string, options?: ApiRequestOptions) {
  const workspaceId = options?.workspaceId ?? getApiContext().workspaceId;
  try {
    const task = mapTaskDetail(await apiClient.get<TaskDetailDto>(`/api/v1/tasks/${id}`, options));
    if (workspaceId) {
      void cachePut(workspaceId, "task", task.id, task);
    }
    return task;
  } catch (error) {
    if (workspaceId && !isOnline() && !(error instanceof ApiError)) {
      const cached = await cacheGetAll(workspaceId, "task");
      const match = cached.find((entry) => entry.id === id);
      if (match) return match.data as TaskDetail;
    }
    throw error;
  }
}

export async function createTask(input: CreateTaskInput, options?: ApiRequestOptions) {
  const body = {
    listId: input.listId,
    title: input.title,
    description: input.description,
    parentId: input.parentId,
    priority: input.priority,
    startDate: toUtcInstant(input.startDate),
    dueDate: toUtcInstant(input.dueDate),
    isMilestone: input.isMilestone,
    assigneeUserIds: input.assigneeUserIds,
    tagIds: input.tagIds,
    statusId: input.statusId,
    taskTypeId: input.taskTypeId,
    customId: input.customId,
  };
  return mapTask(await apiClient.post<TaskDto>("/api/v1/tasks", body, options));
}

export async function updateTask(id: string, patch: UpdateTaskPatch, options?: ApiRequestOptions) {
  const body = {
    ...patch,
    ...("startDate" in patch ? { startDate: toUtcInstant(patch.startDate) } : {}),
    ...("dueDate" in patch ? { dueDate: toUtcInstant(patch.dueDate) } : {}),
  };
  return mapTask(await apiClient.patch<TaskDto>(`/api/v1/tasks/${id}`, body, options));
}

export async function moveTask(id: string, input: MoveTaskInput) {
  return mapTask(await apiClient.post<TaskDto>(`/api/v1/tasks/${id}/move`, input));
}

export async function completeTask(id: string) {
  return mapTask(await apiClient.post<TaskDto>(`/api/v1/tasks/${id}/complete`, {}));
}

export async function reopenTask(id: string) {
  return mapTask(await apiClient.post<TaskDto>(`/api/v1/tasks/${id}/reopen`, {}));
}

/**  . uc(s)napshot the task's current start/due date as its baseline. */
export async function setTaskBaseline(id: string) {
  return mapTask(await apiClient.post<TaskDto>(`/api/v1/tasks/${id}/baseline`, {}));
}

/** Creates a copy of the task (title, assignees, tags, checklists, custom fields) in the same list. */
export async function duplicateTask(id: string) {
  return mapTask(await apiClient.post<TaskDto>(`/api/v1/tasks/${id}/duplicate`, {}));
}

/**  . uc(c)reates a copy of the task (same fields as `duplicateTask`) placed into a different list. */
export async function copyTaskToList(id: string, targetListId: string) {
  return mapTask(await apiClient.post<TaskDto>(`/api/v1/tasks/${id}/copy`, { targetListId }));
}

/**  . uc(m)oves the source task's checklists/attachments/custom-field values onto the target and
 * archives the source. Returns the (updated) target task. */
export async function mergeTask(sourceTaskId: string, targetTaskId: string) {
  return mapTask(await apiClient.post<TaskDto>(`/api/v1/tasks/${sourceTaskId}/merge`, { targetTaskId }));
}

// ---------------------------------------------------------------------------
// Multi-list task membership.
// ---------------------------------------------------------------------------

export async function listTaskLists(taskId: string) {
  return apiClient.get<TaskListMembershipInfo[]>(`/api/v1/tasks/${taskId}/lists`);
}

export async function addTaskToList(taskId: string, listId: string) {
  return apiClient.post<TaskListMembershipInfo[]>(`/api/v1/tasks/${taskId}/lists`, { listId });
}

export async function removeTaskFromList(taskId: string, listId: string) {
  return apiClient.delete<TaskListMembershipInfo[]>(`/api/v1/tasks/${taskId}/lists/${listId}`);
}

// ---------------------------------------------------------------------------
// Generic "relates to" links.
// ---------------------------------------------------------------------------

export async function addTaskRelation(taskId: string, relatedTaskId: string) {
  return apiClient.post<TaskRelationInfo>(`/api/v1/tasks/${taskId}/relations`, { relatedTaskId });
}

export async function removeTaskRelation(taskId: string, relatedTaskId: string) {
  await apiClient.delete<void>(`/api/v1/tasks/${taskId}/relations/${relatedTaskId}`);
}

// ---------------------------------------------------------------------------
// Team assignees (alongside individual `addAssignee`/`removeAssignee` above).
// ---------------------------------------------------------------------------

export async function addTeamAssignee(taskId: string, teamId: string) {
  return mapTask(await apiClient.post<TaskDto>(`/api/v1/tasks/${taskId}/team-assignees`, { teamId }));
}

export async function removeTeamAssignee(taskId: string, teamId: string) {
  return mapTask(await apiClient.delete<TaskDto>(`/api/v1/tasks/${taskId}/team-assignees/${teamId}`));
}

// ---------------------------------------------------------------------------
// Workspace-configurable task types.
// ---------------------------------------------------------------------------

export async function listTaskTypes() {
  return apiClient.get<TaskType[]>("/api/v1/task-types");
}

export async function createTaskType(input: { name: string; color?: string; icon?: string }) {
  return apiClient.post<TaskType>("/api/v1/task-types", input);
}

export async function updateTaskType(
  id: string,
  input: { name: string; color?: string; icon?: string },
) {
  return apiClient.patch<TaskType>(`/api/v1/task-types/${id}`, input);
}

// ---------------------------------------------------------------------------
// Estimates — reuses Planning's existing per-task estimate (already wired into Reporting's
// EstimateVsActual widget); we do not add a second estimate field on WorkItem.
// ---------------------------------------------------------------------------

export async function getTaskEstimate(taskId: string) {
  return apiClient.get<{ taskId: string; estimateSeconds: number }>(`/api/v1/tasks/${taskId}/estimate`);
}

export async function setTaskEstimate(taskId: string, estimateSeconds: number) {
  return apiClient.put<{ taskId: string; estimateSeconds: number }>(
    `/api/v1/tasks/${taskId}/estimate`,
    { estimateSeconds },
  );
}

// ---------------------------------------------------------------------------
// Minimal email-to-task ingestion (manual-trigger vertical slice; the production upgrade path is a
// real inbound mailbox).
// ---------------------------------------------------------------------------

export async function ingestEmailAsTask(
  listId: string,
  input: { from: string; subject: string; body: string },
) {
  return mapTask(await apiClient.post<TaskDto>(`/api/v1/lists/${listId}/email-ingest`, input));
}

export async function listReminders(taskId: string) {
  return apiClient.get<Reminder[]>(`/api/v1/tasks/${taskId}/reminders`);
}

export async function createReminder(taskId: string, remindAtUtc: string, note?: string) {
  return apiClient.post<Reminder>(`/api/v1/tasks/${taskId}/reminders`, { remindAtUtc, note });
}

export async function deleteReminder(reminderId: string) {
  await apiClient.delete<void>(`/api/v1/reminders/${reminderId}`);
}

/** Soft delete — the row stays in the database and `restoreTask` brings it back. */
export async function deleteTask(id: string) {
  await apiClient.delete<void>(`/api/v1/tasks/${id}`);
}

export async function restoreTask(id: string) {
  await apiClient.post<void>(`/api/v1/tasks/${id}/restore`, {});
}

export async function archiveTask(id: string) {
  return mapTask(await apiClient.post<TaskDto>(`/api/v1/tasks/${id}/archive`, {}));
}

export async function unarchiveTask(id: string) {
  return mapTask(await apiClient.post<TaskDto>(`/api/v1/tasks/${id}/unarchive`, {}));
}

/** One call per operation set; the API returns how many tasks it actually touched. */
export async function bulkUpdateTasks(input: BulkTaskInput) {
  return apiClient.post<{ affected: number }, BulkTaskInput>("/api/v1/tasks/bulk", {
    ...input,
    dueDate: toUtcInstant(input.dueDate),
  });
}

export async function listCustomFields() {
  return apiClient.get<CustomFieldDefinition[]>("/api/v1/custom-fields");
}

export type CreateCustomFieldInput = {
  name: string;
  type: string;
  scope: "Workspace" | "Space" | "Folder" | "List";
  scopeId?: string;
  isRequired?: boolean;
  options?: Array<{ label: string; color?: string }>;
  /** Formula fields only. */
  formulaExpression?: string;
  /** Rollup fields only. */
  rollupSourceType?: string;
  rollupSourceFieldId?: string;
  rollupTargetFieldId?: string;
  rollupFunction?: string;
};

/**  . uc(F)older is a valid scope — a field defined here is inherited by every List nested under it. */
export async function createCustomField(input: CreateCustomFieldInput) {
  return apiClient.post<CustomFieldDefinition>("/api/v1/custom-fields", {
    ...input,
    isRequired: input.isRequired ?? false,
  });
}

/** `value` is the raw string the API parses per definition type; `null` clears the value. */
export async function setCustomFieldValue(taskId: string, definitionId: string, value: string | null) {
  await apiClient.put<void>(`/api/v1/tasks/${taskId}/custom-fields/${definitionId}`, { value });
}

/**  . uc(f)ull replacement of a Relationship field's linked tasks. */
export async function setCustomFieldRelationships(taskId: string, definitionId: string, relatedTaskIds: string[]) {
  return apiClient.put<CustomFieldValue>(`/api/v1/tasks/${taskId}/custom-fields/${definitionId}/relationships`, {
    relatedTaskIds,
  });
}

export async function addDependency(taskId: string, dependsOnTaskId: string, type: DependencyType) {
  return apiClient.post<TaskDependency>(`/api/v1/tasks/${taskId}/dependencies`, {
    dependsOnTaskId,
    type,
  });
}

export async function removeDependency(taskId: string, dependencyId: string) {
  await apiClient.delete<void>(`/api/v1/tasks/${taskId}/dependencies/${dependencyId}`);
}

/** My Work "Assigned to me" section. `workspaceId`, when given, both scopes the query (via
 * `?workspaceId=`) and overrides the ambient `X-Workspace` header for this call — so My Work can show
 * a Workspace other than the one currently active elsewhere in the app shell (product spec section 15:
 * "personal cross-Workspace or Workspace-filtered view"). */
export async function listMyTasks(workspaceId?: string) {
  const query = workspaceId ? `?workspaceId=${workspaceId}` : "";
  return (await apiClient.get<TaskDto[]>(`/api/v1/tasks/mine${query}`, { workspaceId })).map(mapTask);
}

/** My Work "Created by me" section: tasks the caller created, regardless of current assignment. */
export async function listTasksCreatedByMe(workspaceId?: string) {
  const query = workspaceId ? `&workspaceId=${workspaceId}` : "";
  return (await apiClient.get<TaskDto[]>(`/api/v1/tasks/mine?scope=created${query}`, { workspaceId })).map(mapTask);
}

/** My Work "Watching" section: tasks the caller watches, regardless of assignment or authorship. */
export async function listTasksWatching(workspaceId?: string) {
  const query = workspaceId ? `&workspaceId=${workspaceId}` : "";
  return (await apiClient.get<TaskDto[]>(`/api/v1/tasks/mine?scope=watching${query}`, { workspaceId })).map(mapTask);
}

/** My Work personal sort/organize preferences (product spec section 15) — global to the caller, not
 * scoped to any one Workspace, since My Work itself can span every Workspace they belong to. */
export async function getMyWorkPreferences() {
  return apiClient.get<MyWorkPreferences>("/api/v1/tasks/mine/preferences");
}

export async function saveMyWorkPreferences(preferences: MyWorkPreferences) {
  return apiClient.put<MyWorkPreferences>("/api/v1/tasks/mine/preferences", preferences);
}

export async function listTags() {
  return apiClient.get<Tag[]>("/api/v1/tags");
}

export async function createTag(name: string, color?: string) {
  return apiClient.post<Tag>("/api/v1/tags", { name, color });
}

export async function addAssignee(taskId: string, userId: string) {
  return mapTask(await apiClient.post<TaskDto>(`/api/v1/tasks/${taskId}/assignees`, { userId }));
}

export async function removeAssignee(taskId: string, userId: string) {
  return mapTask(await apiClient.delete<TaskDto>(`/api/v1/tasks/${taskId}/assignees/${userId}`));
}

/** Full replacement — the API has no add/remove tag routes. */
export async function setTaskTags(taskId: string, tagIds: string[]) {
  return mapTask(await apiClient.put<TaskDto>(`/api/v1/tasks/${taskId}/tags`, { tagIds }));
}

export async function addWatcher(taskId: string, userId: string) {
  await apiClient.post<void>(`/api/v1/tasks/${taskId}/watchers`, { userId });
}

export async function removeWatcher(taskId: string, userId: string) {
  await apiClient.delete<void>(`/api/v1/tasks/${taskId}/watchers/${userId}`);
}

export async function addChecklist(taskId: string, name: string) {
  await apiClient.post<void>(`/api/v1/tasks/${taskId}/checklists`, { name });
}

export async function addChecklistItem(checklistId: string, content: string) {
  await apiClient.post<void>(`/api/v1/checklists/${checklistId}/items`, { content });
}

export async function setChecklistItemResolved(itemId: string, isResolved: boolean) {
  await apiClient.patch<void>(`/api/v1/checklist-items/${itemId}`, { isResolved });
}

export async function listAttachments(taskId: string) {
  return apiClient.get<Attachment[]>(`/api/v1/tasks/${taskId}/attachments`);
}

export async function uploadAttachment(taskId: string, file: File) {
  const body = new FormData();
  body.append("file", file);
  return apiClient.post<Attachment, FormData>(`/api/v1/tasks/${taskId}/attachments`, body);
}

export async function deleteAttachment(id: string) {
  await apiClient.delete<void>(`/api/v1/attachments/${id}`);
}

/** Plain `<a href>` target — the proxy re-applies the workspace header from query params. */
export function attachmentDownloadHref(id: string) {
  return proxyHref(`/attachments/${id}/download`);
}

// ---------------------------------------------------------------------------
// Structure CRUD — spaces, folders, lists (WorkStructureEndpoints.cs).
// Shapes mirror WorkRequests.cs; every optional field is nullable server-side,
// so omitting a key leaves the stored value untouched on PATCH.
// ---------------------------------------------------------------------------

export type SpaceInput = {
  name: string;
  description?: string;
  color?: string;
  icon?: string;
};

/** `options` is only for onboarding, which writes before AppContext has resolved the new workspace. */
export async function createSpace(input: SpaceInput, options?: ApiRequestOptions) {
  return apiClient.post<Space>("/api/v1/spaces", input, options);
}

export async function updateSpace(id: string, patch: Partial<SpaceInput> & { position?: number }) {
  return apiClient.patch<Space>(`/api/v1/spaces/${id}`, patch);
}

export async function archiveSpace(id: string) {
  await apiClient.post<void>(`/api/v1/spaces/${id}/archive`);
}

export async function restoreSpace(id: string) {
  await apiClient.post<void>(`/api/v1/spaces/${id}/restore`);
}

export async function deleteSpace(id: string) {
  await apiClient.delete<void>(`/api/v1/spaces/${id}`);
}

export async function createFolder(spaceId: string, name: string, parentFolderId?: string) {
  return apiClient.post<Folder>(`/api/v1/spaces/${spaceId}/folders`, { name, parentFolderId });
}

export async function renameFolder(folderId: string, name: string) {
  return apiClient.patch<Folder>(`/api/v1/folders/${folderId}`, { name });
}

export async function deleteFolder(folderId: string) {
  return apiClient.delete<void>(`/api/v1/folders/${folderId}`);
}

/**  . uc(r)e-parents a folder to arbitrary depth; `null` moves it to top-level. The API rejects a move that would create a cycle. */
export async function moveFolder(folderId: string, parentFolderId: string | null) {
  return apiClient.post<Folder>(`/api/v1/folders/${folderId}/move`, { parentFolderId });
}

/**  . uc(d)eep-copies the folder — every subfolder (any depth) and every list/task within them. */
export async function duplicateFolder(folderId: string) {
  return apiClient.post<Folder>(`/api/v1/folders/${folderId}/duplicate`, {});
}

/**  . uc(c)opies the list's tasks (fields, assignees, watchers, tags, checklists, custom fields). */
export async function duplicateList(listId: string) {
  return apiClient.post<TaskList>(`/api/v1/lists/${listId}/duplicate`, {});
}

/**  . uc(t)his list's own + Space/Workspace + ancestor-Folder-inherited custom fields. */
export async function listEffectiveCustomFields(listId: string) {
  return apiClient.get<CustomFieldDefinition[]>(`/api/v1/lists/${listId}/custom-fields`);
}

// ---------------------------------------------------------------------------
// Default views: favourites: templates.
// ---------------------------------------------------------------------------

export async function listViews() {
  return apiClient.get<SavedView[]>("/api/v1/views");
}

export async function createView(input: {
  viewType: string;
  scopeType: "Workspace" | "Space" | "Folder" | "List";
  scopeId?: string;
  name: string;
  config?: string;
  isPrivate?: boolean;
}) {
  return apiClient.post<SavedView>("/api/v1/views", { ...input, isPrivate: input.isPrivate ?? false });
}

export async function updateView(id: string, patch: { name?: string; config?: string; isPrivate?: boolean }) {
  return apiClient.patch<SavedView>(`/api/v1/views/${id}`, {
    name: patch.name,
    configJson: patch.config,
    isPrivate: patch.isPrivate,
  });
}

export async function setSpaceDefaultView(spaceId: string, viewId: string | null) {
  return apiClient.put<Space>(`/api/v1/spaces/${spaceId}/default-view`, { viewId });
}

export async function setFolderDefaultView(folderId: string, viewId: string | null) {
  return apiClient.put<Folder>(`/api/v1/folders/${folderId}/default-view`, { viewId });
}

export async function setListDefaultView(listId: string, viewId: string | null) {
  return apiClient.put<TaskList>(`/api/v1/lists/${listId}/default-view`, { viewId });
}

export async function listFavorites() {
  return apiClient.get<WorkFavorite[]>("/api/v1/favorites");
}

/** Toggles a favourite on/off; resolves to the new state. */
export async function toggleFavorite(resourceType: string, resourceId: string) {
  const result = await apiClient.post<{ isFavorited: boolean }>("/api/v1/favorites/toggle", { resourceType, resourceId });
  return result.isFavorited;
}

export async function listRecentItems(limit?: number) {
  const query = limit ? `?limit=${limit}` : "";
  return apiClient.get<RecentItem[]>(`/api/v1/recent-items${query}`);
}

/** Fire-and-forget from the caller's point of view; records/bumps a "recently viewed" entry. */
export async function recordRecentItem(resourceType: string, resourceId: string) {
  await apiClient.post("/api/v1/recent-items", { resourceType, resourceId });
}

export async function listTemplates() {
  return apiClient.get<WorkTemplate[]>("/api/v1/templates");
}

export async function saveAsTemplate(resourceType: WorkTemplateResourceType, sourceResourceId: string, name: string) {
  return apiClient.post<WorkTemplate>("/api/v1/templates", { resourceType, sourceResourceId, name });
}

export async function createFromTemplate(
  templateId: string,
  input: { name: string; spaceId?: string; folderId?: string },
) {
  return apiClient.post<{ resourceType: string; id: string; name: string }>(
    `/api/v1/templates/${templateId}/apply`,
    input,
  );
}

export type CreateListInput = {
  spaceId: string;
  folderId?: string;
  name: string;
  description?: string;
  statusSchemeId?: string;
};

export async function createList(input: CreateListInput, options?: ApiRequestOptions) {
  return apiClient.post<TaskList>("/api/v1/lists", input, options);
}


export async function updateList(id: string, patch: { name?: string; description?: string }) {
  return apiClient.patch<TaskList>(`/api/v1/lists/${id}`, patch);
}

export async function archiveList(id: string) {
  await apiClient.post<void>(`/api/v1/lists/${id}/archive`);
}

export async function restoreList(id: string) {
  await apiClient.post<void>(`/api/v1/lists/${id}/restore`);
}

export async function deleteList(id: string) {
  await apiClient.delete<void>(`/api/v1/lists/${id}`);
}
