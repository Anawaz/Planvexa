namespace Planvexa.Modules.Goals.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>Groups Goals for organization (mirrors WorkManagement's Space→Folder grouping shape, but
/// far simpler — Goals are workspace-flat, a Folder is just a label to group them under).</summary>
public sealed class GoalFolder : Entity, IAggregateRoot, IWorkspaceOwned
{
    private GoalFolder()
    {
    }

    private GoalFolder(Guid id, Guid workspaceId, string name, Guid createdByUserId, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static GoalFolder Create(Guid id, Guid workspaceId, string name, Guid createdByUserId, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        return new GoalFolder(id, workspaceId, name.Trim(), createdByUserId, nowUtc);
    }

    public void Rename(string name)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Name = name.Trim();
    }
}

/// <summary>A lightweight, goal-scoped comment (see <see cref="Goal"/>'s doc comment for why this is not
/// wired through the Collaboration module).</summary>
public sealed class GoalComment : Entity, IAggregateRoot, IWorkspaceOwned
{
    private GoalComment()
    {
    }

    private GoalComment(Guid id, Guid workspaceId, Guid goalId, Guid authorUserId, string body, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        GoalId = goalId;
        AuthorUserId = authorUserId;
        Body = body;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid GoalId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static GoalComment Create(Guid id, Guid workspaceId, Guid goalId, Guid authorUserId, string body, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(body, nameof(body));
        Guard.AgainstEmpty(goalId, nameof(goalId));
        Guard.AgainstEmpty(authorUserId, nameof(authorUserId));
        return new GoalComment(id, workspaceId, goalId, authorUserId, body.Trim(), nowUtc);
    }
}
