namespace Planvexa.Modules.WorkManagement.Application;

using Planvexa.Modules.WorkManagement.Domain;

public interface ISpaceStore
{
    void Add(Space space);
    Task<Space?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Space>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task<double?> MaxPositionAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface IFolderStore
{
    void Add(Folder folder);
    Task<Folder?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Folder>> ListBySpaceAsync(Guid spaceId, CancellationToken ct = default);
    Task<double?> MaxPositionAsync(Guid spaceId, CancellationToken ct = default);
}

public interface ITaskListStore
{
    void Add(TaskList list);
    Task<TaskList?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TaskList>> ListBySpaceAsync(Guid spaceId, CancellationToken ct = default);
    Task<double?> MaxPositionAsync(Guid spaceId, CancellationToken ct = default);
}

public interface IStatusSchemeStore
{
    void Add(StatusScheme scheme);
    Task<StatusScheme?> FindAsync(Guid id, CancellationToken ct = default);
    Task<StatusScheme?> FindDefaultAsync(Guid workspaceId, CancellationToken ct = default);
    Task<IReadOnlyList<StatusScheme>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task<StatusDefinition?> FindStatusAsync(Guid statusId, CancellationToken ct = default);
}

public interface ITagStore
{
    void Add(Tag tag);
    Task<IReadOnlyList<Tag>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ExistingTagIdsAsync(Guid workspaceId, IReadOnlyCollection<Guid> tagIds, CancellationToken ct = default);
}

public interface IWorkItemStore
{
    void Add(WorkItem task);
    Task<WorkItem?> FindAsync(Guid id, CancellationToken ct = default);
    Task<WorkItem?> FindWithRelationsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WorkItem>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    /// <summary>Every task with a <see cref="TaskListMembership"/> row in this list (primary or
    /// not), ordered by that membership's own position — NOT filtered by WorkItem.ListId, so a task
    /// added to a second list shows up here too.</summary>
    Task<IReadOnlyList<WorkItem>> ListByListAsync(Guid listId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkItem>> ListAssignedToUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Highest TaskListMembership.Position in this list (append-position for a new membership).</summary>
    Task<double?> MaxPositionAsync(Guid listId, CancellationToken ct = default);

    /// <summary>True if another task in this list already has this CustomId (excluding
    /// <paramref name="excludeTaskId"/> itself, for update-in-place checks). Lets the service reject a
    /// collision with a clean 400 before it hits the DB's unique index as a raw constraint violation.</summary>
    Task<bool> CustomIdExistsAsync(Guid listId, string customId, Guid? excludeTaskId, CancellationToken ct = default);

    /// <summary>Offline-mutation-outbox replay guard: the task previously created with this Idempotency-Key
    /// in this workspace, if any (see WorkItem.IdempotencyKey's doc comment).</summary>
    Task<WorkItem?> FindByIdempotencyKeyAsync(Guid workspaceId, string key, CancellationToken ct = default);

    /// <summary>Direct (immediate) subtasks of a task — WorkItem.ParentId == parentId —
    /// for Subtasks-sourced Rollup fields. Not the full descendant tree.</summary>
    Task<IReadOnlyList<WorkItem>> ListSubtasksAsync(Guid parentId, CancellationToken ct = default);

    /// <summary>Due-date automation trigger: open tasks with DueDate in [fromUtc, toUtc).</summary>
    Task<IReadOnlyList<WorkItem>> ListDueBetweenAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>SLA automation trigger: every open task's (id, StatusId, most-recent
    /// status_changed activity timestamp or CreatedAtUtc if none).</summary>
    Task<IReadOnlyList<(Guid TaskId, Guid StatusId, DateTimeOffset EnteredAtUtc)>> ListOpenTaskStatusAgesAsync(CancellationToken ct = default);
}

/// <summary>A task's many-to-many membership in Lists (see TaskListMembership's doc comment).</summary>
public interface ITaskListMembershipStore
{
    void Add(TaskListMembership membership);
    void Remove(TaskListMembership membership);
    Task<TaskListMembership?> FindAsync(Guid taskId, Guid listId, CancellationToken ct = default);
    Task<TaskListMembership?> FindPrimaryAsync(Guid taskId, CancellationToken ct = default);
    Task<IReadOnlyList<TaskListMembership>> ListForTaskAsync(Guid taskId, CancellationToken ct = default);
    Task<int> CountForListAsync(Guid listId, CancellationToken ct = default);
}

/// <summary>Workspace-configurable task types (see TaskType's doc comment).</summary>
public interface ITaskTypeStore
{
    void Add(TaskType type);
    Task<TaskType?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TaskType>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task<TaskType?> FindBuiltInAsync(Guid workspaceId, CancellationToken ct = default);
}

/// <summary>Free-form "relates to" links between tasks (see TaskRelation's doc comment).</summary>
public interface ITaskRelationStore
{
    void Add(TaskRelation relation);
    void Remove(TaskRelation relation);
    Task<TaskRelation?> FindAsync(Guid taskId, Guid relatedTaskId, CancellationToken ct = default);

    /// <summary>Relations where the task is on either side of the pair.</summary>
    Task<IReadOnlyList<TaskRelation>> ListForTaskAsync(Guid taskId, CancellationToken ct = default);
}

public interface IDependencyStore
{
    void Add(TaskDependency dependency);
    void Remove(TaskDependency dependency);
    Task<TaskDependency?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TaskDependency>> ListForTaskAsync(Guid taskId, CancellationToken ct = default);

    /// <summary>Ids of tasks that block the given task and are not yet completed.</summary>
    Task<IReadOnlyList<Guid>> IncompleteBlockersAsync(Guid taskId, CancellationToken ct = default);
}

public interface IChecklistStore
{
    void Add(TaskChecklist checklist);
    Task<TaskChecklist?> FindAsync(Guid id, CancellationToken ct = default);
    Task<TaskChecklistItem?> FindItemAsync(Guid itemId, CancellationToken ct = default);
    Task<double?> MaxItemPositionAsync(Guid checklistId, CancellationToken ct = default);
    Task<IReadOnlyList<TaskChecklist>> ListForTaskAsync(Guid taskId, CancellationToken ct = default);
}

public interface ICustomFieldStore
{
    void Add(CustomFieldDefinition definition);
    void AddValue(CustomFieldValue value);
    Task<CustomFieldDefinition?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CustomFieldDefinition>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task<CustomFieldValue?> FindValueAsync(Guid taskId, Guid definitionId, CancellationToken ct = default);
    Task<IReadOnlyList<CustomFieldValue>> ListValuesForTaskAsync(Guid taskId, CancellationToken ct = default);

    // Relationship-type field links — a dedicated join table keyed by field definition
    // (see CustomFieldRelationshipValue's doc comment).
    void AddRelationshipValue(CustomFieldRelationshipValue value);
    void RemoveRelationshipValue(CustomFieldRelationshipValue value);
    Task<IReadOnlyList<CustomFieldRelationshipValue>> ListRelationshipValuesAsync(Guid taskId, Guid definitionId, CancellationToken ct = default);
}

public interface IRecurringTaskStore
{
    void Add(RecurringTaskDefinition definition);
    void AddOccurrence(RecurringOccurrence occurrence);
    Task<RecurringTaskDefinition?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> OccurrenceExistsAsync(Guid definitionId, string occurrenceKey, CancellationToken ct = default);
    Task<IReadOnlyList<RecurringTaskDefinition>> ListDueAsync(DateTimeOffset nowUtc, int max, CancellationToken ct = default);
}

public interface ISavedViewStore
{
    void Add(SavedView view);
    Task<SavedView?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SavedView>> ListForUserAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);
}

public interface IAttachmentStore
{
    void Add(TaskAttachment attachment);
    void Remove(TaskAttachment attachment);
    Task<TaskAttachment?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TaskAttachment>> ListForTaskAsync(Guid taskId, CancellationToken ct = default);
}

/// <summary>
/// Raw (not yet permission-filtered) name/title-match candidates, one method per searchable type.
/// Returns entities rather than projections so the caller (SearchService) can run each candidate through
/// WorkManagementAuthorizer.CanReadAsync before it is ever shown to the caller — see SearchService's
/// doc comment for why that filter is not optional.
/// </summary>
public interface ISearchStore
{
    Task<IReadOnlyList<Space>> SearchSpacesAsync(Guid workspaceId, string contains, int take, CancellationToken ct = default);
    Task<IReadOnlyList<Folder>> SearchFoldersAsync(Guid workspaceId, string contains, int take, CancellationToken ct = default);
    Task<IReadOnlyList<TaskList>> SearchListsAsync(Guid workspaceId, string contains, int take, CancellationToken ct = default);
    Task<IReadOnlyList<WorkItem>> SearchTasksAsync(Guid workspaceId, string contains, string startsWith, int take, CancellationToken ct = default);
}

public interface IActivityStore
{
    void Add(TaskActivityEvent activity);
    Task<IReadOnlyList<TaskActivityEvent>> ListForTaskAsync(Guid taskId, int max, CancellationToken ct = default);

    /// <summary>Newest-first page of activity events across every task in the workspace (not
    /// scoped to one task), keyset-paginated on CreatedAtUtc &lt; <paramref name="beforeUtc"/>. The
    /// caller (WorkspaceActivityService) still has to ACL-filter the page per task -- this store method
    /// does not know about privacy/ACL, same separation as every other *Store in this module.</summary>
    Task<IReadOnlyList<TaskActivityEvent>> ListByWorkspaceAsync(
        Guid workspaceId, DateTimeOffset beforeUtc, int take, Guid? actorUserId,
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct = default);
}

public interface IWorkTemplateStore
{
    void Add(WorkTemplate template);
    Task<WorkTemplate?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WorkTemplate>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface IWorkFavoriteStore
{
    void Add(WorkFavorite favorite);
    void Remove(WorkFavorite favorite);
    Task<WorkFavorite?> FindAsync(Guid workspaceId, Guid userId, string resourceType, Guid resourceId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkFavorite>> ListForUserAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);
}

/// <summary>P8: per-user "recently viewed" tracking, free-form ResourceType (same convention as
/// WorkFavorite — see its doc comment). Upsert-on-view + a bounded overflow list the service deletes to
/// cap rows per user.</summary>
public interface IRecentItemStore
{
    void Add(RecentItem item);
    void Remove(RecentItem item);
    Task<RecentItem?> FindAsync(Guid workspaceId, Guid userId, string resourceType, Guid resourceId, CancellationToken ct = default);
    Task<IReadOnlyList<RecentItem>> ListForUserAsync(Guid workspaceId, Guid userId, int take, CancellationToken ct = default);

    /// <summary>Rows beyond the most-recent <paramref name="keep"/> for this user, newest-first order not
    /// guaranteed — the service deletes every row returned here.</summary>
    Task<IReadOnlyList<RecentItem>> ListOverflowAsync(Guid workspaceId, Guid userId, int keep, CancellationToken ct = default);
}

public interface IReminderStore
{
    void Add(TaskReminder reminder);
    void Remove(TaskReminder reminder);
    Task<TaskReminder?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TaskReminder>> ListForTaskAsync(Guid taskId, Guid userId, CancellationToken ct = default);

    /// <summary>Cross-workspace: unsent reminders due at or before <paramref name="nowUtc"/> (dispatcher).</summary>
    Task<IReadOnlyList<TaskReminder>> ListDueAsync(DateTimeOffset nowUtc, int max, CancellationToken ct = default);
}

public interface IImportJobStore
{
    void Add(ImportJob job);
    Task<ImportJob?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ImportJob>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface IImportJobRowStore
{
    void Add(ImportJobRow row);
    Task<IReadOnlyList<ImportJobRow>> ListByJobAsync(Guid importJobId, CancellationToken ct = default);

    /// <summary>Rows still needing work for validate/commit — excludes rows already in a terminal state
    /// for that pass, so re-invoking validate/commit after an interruption only touches what's left
    /// (AGENTS.md rule 13's "resumable" requirement).</summary>
    Task<IReadOnlyList<ImportJobRow>> ListPendingOrInvalidAsync(Guid importJobId, CancellationToken ct = default);

    Task<IReadOnlyList<ImportJobRow>> ListValidNotCommittedAsync(Guid importJobId, CancellationToken ct = default);
}
