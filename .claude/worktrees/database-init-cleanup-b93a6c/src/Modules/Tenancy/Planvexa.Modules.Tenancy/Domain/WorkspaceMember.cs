namespace Planvexa.Modules.Tenancy.Domain;

using Planvexa.BuildingBlocks.Domain;

public sealed class WorkspaceMember : Entity, IWorkspaceOwned
{
    private WorkspaceMember()
    {
    }

    private WorkspaceMember(
        Guid id, Guid workspaceId, Guid userId, MembershipRole role, Guid? roleId, bool isGuest, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        Role = role;
        RoleId = roleId;
        IsGuest = isGuest;
        Status = MembershipStatus.Active;
        JoinedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public MembershipRole Role { get; private set; }

    /// <summary>
    /// The workspace's tenancy.roles row backing this member's permissions (ADR-0003). Null
    /// means "use the fast-path <see cref="Role"/> enum value" (compatibility; also true briefly for a
    /// member created before role backfill runs). Non-null — the normal case after backfill/seeding —
    /// means "resolve permissions from this role's grants", which lets a workspace assign a custom role
    /// later without touching this column's shape.
    /// </summary>
    public Guid? RoleId { get; private set; }

    public bool IsGuest { get; private set; }
    public MembershipStatus Status { get; private set; }
    public DateTimeOffset JoinedAtUtc { get; private set; }

    public static WorkspaceMember Create(
        Guid id, Guid workspaceId, Guid userId, MembershipRole role, DateTimeOffset nowUtc, Guid? roleId = null)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(workspaceId, nameof(workspaceId));
        Guard.AgainstEmpty(userId, nameof(userId));
        return new WorkspaceMember(id, workspaceId, userId, role, roleId, role == MembershipRole.Guest, nowUtc);
    }

    public void ChangeRole(MembershipRole role, Guid? roleId = null)
    {
        Role = role;
        RoleId = roleId;
        IsGuest = role == MembershipRole.Guest;
    }

    public void Deactivate() => Status = MembershipStatus.Deactivated;

    public void Reactivate() => Status = MembershipStatus.Active;
}
