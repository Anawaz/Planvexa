namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.WorkManagement.Domain.Events;

/// <summary>
/// A Task (named <c>WorkItem</c> in code because the BCL reserves <c>Task</c>). The aggregate root of
/// task management: assignees, watchers and tags are managed through it. Subtasks are modelled via
/// <see cref="ParentId"/>. Completion is defined by the assigned status' category and guarded by
/// dependencies at the application layer.
/// </summary>
public sealed class WorkItem : WorkEntity, IAggregateRoot
{
    private readonly List<TaskAssignee> _assignees = new();
    private readonly List<TaskWatcher> _watchers = new();
    private readonly List<TaskTag> _tags = new();
    private readonly List<TaskTeamAssignee> _teamAssignees = new();

    private WorkItem()
    {
    }

    private WorkItem(
        Guid id, Guid workspaceId, Guid spaceId, Guid listId, Guid? parentId,
        long sequence, string title, Guid statusId, bool statusIsComplete, DateTimeOffset nowUtc, Guid createdBy,
        string? idempotencyKey)
        : base(id)
    {
        WorkspaceId = workspaceId;
        SpaceId = spaceId;
        ListId = listId;
        ParentId = parentId;
        Sequence = sequence;
        Title = title;
        StatusId = statusId;
        IsCompleted = statusIsComplete;
        Priority = TaskPriority.None;
        CreatedAtUtc = nowUtc;
        CreatedByUserId = createdBy;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>
    /// The task's PRIMARY list/space — kept in sync with the <c>IsPrimary</c> row of
    /// <see cref="TaskListMembership"/> by the application service. Every earlier call site (status
    /// resolution, breadcrumbs, search, "my tasks", direct-by-id privacy resolution) reads these two
    /// properties and means "the task's one true list", so they are preserved unchanged; TRUE many-to-many
    /// membership (a task appearing in several Lists) lives only in TaskListMembership, queried through
    /// ITaskListMembershipStore. See WorkItemService's XML docs for how Create/Move/AddToList/RemoveFromList
    /// keep the two in sync.
    /// </summary>
    public Guid SpaceId { get; private set; }
    public Guid ListId { get; private set; }
    public Guid? ParentId { get; private set; }

    /// <summary>Human-friendly per-list task number.</summary>
    public long Sequence { get; private set; }

    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid StatusId { get; private set; }
    public TaskPriority Priority { get; private set; }
    public DateTimeOffset? StartDate { get; private set; }
    public DateTimeOffset? DueDate { get; private set; }

    /// <summary>Gantt baselines: a snapshot of <see cref="StartDate"/>/<see cref="DueDate"/> taken
    /// explicitly via <see cref="SetBaseline"/>, left untouched by ordinary reschedules -- lets the Gantt
    /// view show "planned vs. current" drift. Null until a baseline is first captured.</summary>
    public DateTimeOffset? BaselineStartDate { get; private set; }
    public DateTimeOffset? BaselineDueDate { get; private set; }
    public bool IsMilestone { get; private set; }
    public bool IsCompleted { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public Guid? CompletedByUserId { get; private set; }

    /// <summary>Workspace-configurable task type; null means "use the workspace's built-in default".</summary>
    public Guid? TaskTypeId { get; private set; }

    /// <summary>An optional user-settable id/key distinct from the auto <see cref="Sequence"/>,
    /// unique per List (see TaskConfigurations' unique index on (ListId, CustomId)).</summary>
    public string? CustomId { get; private set; }

    /// <summary>Offline-mutation-outbox replay guard: when the client supplies an Idempotency-Key header
    /// on task creation, it is stored here (unique per workspace, see TaskConfigurations' filtered index)
    /// so a replayed create after a lost response returns the original task instead of a duplicate — same
    /// pattern as AiAssistService/FormSubmission's idempotency keys.</summary>
    public string? IdempotencyKey { get; private set; }

    // NOT a field here — Planning.TaskEstimate (planning.task_estimates,
    // GET/PUT /api/v1/tasks/{taskId}/estimate, already wired into Reporting's EstimateVsActual widget via
    // IPlanningQueries.EstimatesForTasksAsync) already IS a real, working per-task estimate concept. The
    // Brief's premise that no such field existed was checked against this codebase and found
    // incorrect; adding a second WorkItem.EstimateMinutes field would just duplicate it. The frontend
    // is wired against the existing endpoint instead.

    public IReadOnlyList<TaskAssignee> Assignees => _assignees.AsReadOnly();
    public IReadOnlyList<TaskWatcher> Watchers => _watchers.AsReadOnly();
    public IReadOnlyList<TaskTag> Tags => _tags.AsReadOnly();
    public IReadOnlyList<TaskTeamAssignee> TeamAssignees => _teamAssignees.AsReadOnly();

    public static WorkItem Create(
        Guid id, Guid workspaceId, Guid spaceId, Guid listId, Guid? parentId,
        long sequence, string title, Guid statusId, bool statusIsComplete, double position, Guid createdBy, DateTimeOffset nowUtc,
        string? idempotencyKey = null)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(listId, nameof(listId));
        Guard.AgainstEmpty(statusId, nameof(statusId));
        Guard.AgainstNullOrWhiteSpace(title, nameof(title));

        var item = new WorkItem(id, workspaceId, spaceId, listId, parentId, sequence, title.Trim(), statusId, statusIsComplete, nowUtc, createdBy, idempotencyKey)
        {
            Position = position,
        };
        item.Raise(new TaskCreatedIntegrationEvent(workspaceId, listId, id, item.Title, createdBy));
        return item;
    }

    public void UpdateDetails(
        string? title, string? description, TaskPriority? priority,
        DateTimeOffset? startDate, DateTimeOffset? dueDate, bool? isMilestone,
        Guid userId, DateTimeOffset nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title.Trim();
        }

        if (description is not null)
        {
            Description = description;
        }

        if (priority.HasValue)
        {
            Priority = priority.Value;
        }

        if (startDate.HasValue)
        {
            StartDate = startDate;
        }

        if (dueDate.HasValue)
        {
            DueDate = dueDate;
        }

        if (isMilestone.HasValue)
        {
            IsMilestone = isMilestone.Value;
        }

        Touch(userId, nowUtc);
    }

    public void ClearDueDate(Guid userId, DateTimeOffset nowUtc)
    {
        DueDate = null;
        Touch(userId, nowUtc);
    }

    /// <summary>Captures the task's CURRENT StartDate/DueDate as its baseline (planned) dates.
    /// Overwrites any prior baseline -- callers that want to preserve an earlier snapshot should not
    /// call this again.</summary>
    public void SetBaseline(Guid userId, DateTimeOffset nowUtc)
    {
        BaselineStartDate = StartDate;
        BaselineDueDate = DueDate;
        Touch(userId, nowUtc);
    }

    /// <summary>Changes status. Completion transitions must go through <see cref="Complete"/>.</summary>
    public void ChangeStatus(Guid newStatusId, bool newStatusIsComplete, Guid userId, DateTimeOffset nowUtc)
    {
        if (StatusId == newStatusId)
        {
            return;
        }

        var fromStatus = StatusId;
        StatusId = newStatusId;

        if (newStatusIsComplete)
        {
            MarkCompletedFlags(userId, nowUtc);
        }
        else if (IsCompleted)
        {
            IsCompleted = false;
            CompletedAtUtc = null;
            CompletedByUserId = null;
        }

        Touch(userId, nowUtc);
        Raise(new TaskStatusChangedIntegrationEvent(WorkspaceId, Id, fromStatus, newStatusId, userId));
    }

    /// <summary>
    /// Completes the task. Callers must first verify no incomplete blocking dependency exists; if
    /// <paramref name="hasIncompleteBlocker"/> is true this throws (the completion guard, ADR aligned).
    /// </summary>
    public void Complete(Guid completeStatusId, bool hasIncompleteBlocker, Guid userId, DateTimeOffset nowUtc)
    {
        if (hasIncompleteBlocker)
        {
            throw new ConflictException("This task cannot be completed while a blocking task is still open.");
        }

        var fromStatus = StatusId;
        StatusId = completeStatusId;
        MarkCompletedFlags(userId, nowUtc);
        Touch(userId, nowUtc);

        if (fromStatus != completeStatusId)
        {
            Raise(new TaskStatusChangedIntegrationEvent(WorkspaceId, Id, fromStatus, completeStatusId, userId));
        }

        Raise(new TaskCompletedIntegrationEvent(WorkspaceId, Id, userId));
    }

    public void Reopen(Guid reopenStatusId, Guid userId, DateTimeOffset nowUtc)
    {
        IsCompleted = false;
        CompletedAtUtc = null;
        CompletedByUserId = null;
        StatusId = reopenStatusId;
        Touch(userId, nowUtc);
    }

    public void MoveTo(Guid listId, Guid spaceId, double position, Guid userId, DateTimeOffset nowUtc)
    {
        ListId = listId;
        SpaceId = spaceId;
        Position = position;
        Touch(userId, nowUtc);
    }

    public void SetTaskType(Guid? taskTypeId, Guid userId, DateTimeOffset nowUtc)
    {
        TaskTypeId = taskTypeId;
        Touch(userId, nowUtc);
    }

    public void SetCustomId(string? customId, Guid userId, DateTimeOffset nowUtc)
    {
        CustomId = string.IsNullOrWhiteSpace(customId) ? null : customId.Trim();
        Touch(userId, nowUtc);
    }

    public bool AddTeamAssignee(Guid id, Guid teamId, Guid actorUserId, DateTimeOffset nowUtc)
    {
        if (_teamAssignees.Any(a => a.TeamId == teamId))
        {
            return false;
        }

        _teamAssignees.Add(new TaskTeamAssignee(id, Id, teamId, nowUtc));
        Touch(actorUserId, nowUtc);
        return true;
    }

    public bool RemoveTeamAssignee(Guid teamId, Guid actorUserId, DateTimeOffset nowUtc)
    {
        var existing = _teamAssignees.FirstOrDefault(a => a.TeamId == teamId);
        if (existing is null)
        {
            return false;
        }

        _teamAssignees.Remove(existing);
        Touch(actorUserId, nowUtc);
        return true;
    }

    public bool AddAssignee(Guid id, Guid assigneeUserId, Guid actorUserId, DateTimeOffset nowUtc)
    {
        if (_assignees.Any(a => a.UserId == assigneeUserId))
        {
            return false;
        }

        _assignees.Add(new TaskAssignee(id, Id, assigneeUserId, nowUtc));
        Touch(actorUserId, nowUtc);
        Raise(new TaskAssignedIntegrationEvent(WorkspaceId, Id, assigneeUserId, actorUserId));
        return true;
    }

    public bool RemoveAssignee(Guid assigneeUserId, Guid actorUserId, DateTimeOffset nowUtc)
    {
        var existing = _assignees.FirstOrDefault(a => a.UserId == assigneeUserId);
        if (existing is null)
        {
            return false;
        }

        _assignees.Remove(existing);
        Touch(actorUserId, nowUtc);
        return true;
    }

    public bool AddWatcher(Guid id, Guid watcherUserId, DateTimeOffset nowUtc)
    {
        if (_watchers.Any(w => w.UserId == watcherUserId))
        {
            return false;
        }

        _watchers.Add(new TaskWatcher(id, Id, watcherUserId, nowUtc));
        return true;
    }

    public bool RemoveWatcher(Guid watcherUserId)
    {
        var existing = _watchers.FirstOrDefault(w => w.UserId == watcherUserId);
        if (existing is null)
        {
            return false;
        }

        _watchers.Remove(existing);
        return true;
    }

    /// <summary>Replaces the full tag set. <paramref name="tagIdFactory"/> supplies join-row ids.</summary>
    public void SetTags(IReadOnlyCollection<Guid> tagIds, Func<Guid> tagIdFactory, Guid userId, DateTimeOffset nowUtc)
    {
        _tags.RemoveAll(t => !tagIds.Contains(t.TagId));
        foreach (var tagId in tagIds)
        {
            if (_tags.All(t => t.TagId != tagId))
            {
                _tags.Add(new TaskTag(tagIdFactory(), Id, tagId));
            }
        }

        Touch(userId, nowUtc);
    }

    private void MarkCompletedFlags(Guid userId, DateTimeOffset nowUtc)
    {
        IsCompleted = true;
        CompletedAtUtc = nowUtc;
        CompletedByUserId = userId;
    }
}
