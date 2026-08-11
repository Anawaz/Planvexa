namespace Planvexa.Modules.Tenancy.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>A named group of workspace members, used for assignment, sharing, mentions and reporting.</summary>
public sealed class Team : Entity, IWorkspaceOwned
{
    private Team()
    {
    }

    private Team(Guid id, Guid workspaceId, string name, string? description, Guid createdBy, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        Description = description;
        CreatedByUserId = createdBy;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsArchived { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Team Create(Guid id, Guid workspaceId, string name, string? description, Guid createdBy, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(workspaceId, nameof(workspaceId));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        return new Team(id, workspaceId, name.Trim(), Normalize(description), createdBy, nowUtc);
    }

    public void Update(string name, string? description)
    {
        Name = Guard.AgainstNullOrWhiteSpace(name, nameof(name)).Trim();
        Description = Normalize(description);
    }

    public void Archive() => IsArchived = true;

    public void Restore() => IsArchived = false;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>A member of a <see cref="Team"/> (a workspace user).</summary>
public sealed class TeamMembership : Entity, IWorkspaceOwned
{
    private TeamMembership()
    {
    }

    private TeamMembership(Guid id, Guid workspaceId, Guid teamId, Guid userId, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        TeamId = teamId;
        UserId = userId;
        AddedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset AddedAtUtc { get; private set; }

    public static TeamMembership Create(Guid id, Guid workspaceId, Guid teamId, Guid userId, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(teamId, nameof(teamId));
        Guard.AgainstEmpty(userId, nameof(userId));
        return new TeamMembership(id, workspaceId, teamId, userId, nowUtc);
    }
}
