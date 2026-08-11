namespace Planvexa.Modules.Tenancy.Domain;

public enum WorkspaceStatus
{
    Active = 0,
    Archived = 1,
}

/// <summary>
/// Coarse role. Ordered so numeric comparison expresses privilege (Owner highest) — mirrored 1:1 by
/// <see cref="Planvexa.SharedContracts.Workspaces.WorkspaceRole"/>; keep both enums in the same order.
/// Since ADR-0003, the DB-backed tenancy.roles/role_permissions model (see
/// Authorization/TenancyPermissions.cs, BuiltInRoles) is the source of truth for permission grants;
/// this enum stays as a compatibility/fast-path value on <see cref="WorkspaceMember"/> (its RoleId
/// resolves to the actual permission set once set).
/// </summary>
public enum MembershipRole
{
    Guest = 0,
    LimitedMember = 1,
    Member = 2,
    Admin = 3,
    Owner = 4,
}

public enum MembershipStatus
{
    Active = 0,
    Deactivated = 1,
}

public enum InvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Revoked = 2,
    Expired = 3,
}
