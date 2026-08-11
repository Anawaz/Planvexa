namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>Join entity: a user assigned to a task.</summary>
public sealed class TaskAssignee : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private TaskAssignee()
    {
    }

    public TaskAssignee(Guid id, Guid taskId, Guid userId, DateTimeOffset assignedAtUtc)
        : base(id)
    {
        TaskId = taskId;
        UserId = userId;
        AssignedAtUtc = assignedAtUtc;
    }

    public Guid TaskId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset AssignedAtUtc { get; private set; }
}

/// <summary>Join entity: a user watching a task for updates.</summary>
public sealed class TaskWatcher : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private TaskWatcher()
    {
    }

    public TaskWatcher(Guid id, Guid taskId, Guid userId, DateTimeOffset addedAtUtc)
        : base(id)
    {
        TaskId = taskId;
        UserId = userId;
        AddedAtUtc = addedAtUtc;
    }

    public Guid TaskId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset AddedAtUtc { get; private set; }
}

/// <summary>Join entity: a tag applied to a task.</summary>
public sealed class TaskTag : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private TaskTag()
    {
    }

    public TaskTag(Guid id, Guid taskId, Guid tagId)
        : base(id)
    {
        TaskId = taskId;
        TagId = tagId;
    }

    public Guid TaskId { get; private set; }
    public Guid TagId { get; private set; }
}

/// <summary>A dependency edge between two tasks (see <see cref="DependencyType"/>).</summary>
public sealed class TaskDependency : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private TaskDependency()
    {
    }

    public TaskDependency(Guid id, Guid taskId, Guid dependsOnTaskId, DependencyType type)
        : base(id)
    {
        TaskId = taskId;
        DependsOnTaskId = dependsOnTaskId;
        Type = type;
    }

    public Guid TaskId { get; private set; }
    public Guid DependsOnTaskId { get; private set; }
    public DependencyType Type { get; private set; }
}

/// <summary>
/// A task's membership in a List. Replaces the old one-list-only <c>WorkItem.ListId</c>
/// FK with true many-to-many — a task can appear in several Lists without duplicating its content. Exactly
/// one membership per task has <see cref="IsPrimary"/> set; the primary list is mirrored onto
/// <see cref="WorkItem.ListId"/>/<see cref="WorkItem.SpaceId"/> for every existing call site (status-scheme
/// resolution, breadcrumbs, search, "my tasks", ...) that reasonably means "the task's one true list" and
/// was never meant to enumerate all memberships. <see cref="Position"/> orders the task within THIS list
/// specifically (a task can sit in different positions in different lists).
/// </summary>
public sealed class TaskListMembership : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private TaskListMembership()
    {
    }

    public TaskListMembership(Guid id, Guid workspaceId, Guid taskId, Guid listId, bool isPrimary, double position, DateTimeOffset addedAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        TaskId = taskId;
        ListId = listId;
        IsPrimary = isPrimary;
        Position = position;
        AddedAtUtc = addedAtUtc;
    }

    public Guid TaskId { get; private set; }
    public Guid ListId { get; private set; }
    public bool IsPrimary { get; private set; }
    public double Position { get; private set; }
    public DateTimeOffset AddedAtUtc { get; private set; }

    public void MarkPrimary(bool isPrimary) => IsPrimary = isPrimary;

    public void Reposition(double position) => Position = position;
}

/// <summary>Join entity: a Team (Tenancy module) assigned to a task, alongside individual user
/// <see cref="TaskAssignee"/>s. Only the team id is stored — WorkManagement must not reference
/// Tenancy's Team entity directly (AGENTS.md rule 7 / module boundary), matching how TaskAssignee.UserId
/// is an opaque id with no cross-module validation either.</summary>
public sealed class TaskTeamAssignee : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private TaskTeamAssignee()
    {
    }

    public TaskTeamAssignee(Guid id, Guid taskId, Guid teamId, DateTimeOffset assignedAtUtc)
        : base(id)
    {
        TaskId = taskId;
        TeamId = teamId;
        AssignedAtUtc = assignedAtUtc;
    }

    public Guid TaskId { get; private set; }
    public Guid TeamId { get; private set; }
    public DateTimeOffset AssignedAtUtc { get; private set; }
}

/// <summary>
/// A free-form, symmetric "relates to" link between two tasks — no scheduling semantics,
/// unlike <see cref="TaskDependency"/>'s Blocks/BlockedBy/WaitingOn. One row per pair; querying "relations
/// for task X" checks both <see cref="TaskId"/> and <see cref="RelatedTaskId"/> so either side of the pair
/// finds it (see IDependencyStore/relation store), avoiding the need for canonical ordering.
/// </summary>
public sealed class TaskRelation : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private TaskRelation()
    {
    }

    public TaskRelation(Guid id, Guid taskId, Guid relatedTaskId, DateTimeOffset createdAtUtc)
        : base(id)
    {
        TaskId = taskId;
        RelatedTaskId = relatedTaskId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid TaskId { get; private set; }
    public Guid RelatedTaskId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
