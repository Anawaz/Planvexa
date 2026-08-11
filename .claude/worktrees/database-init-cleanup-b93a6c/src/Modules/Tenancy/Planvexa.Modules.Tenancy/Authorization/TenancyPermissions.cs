namespace Planvexa.Modules.Tenancy.Authorization;

using Planvexa.Modules.Tenancy.Application;
using Planvexa.Modules.Tenancy.Domain;

/// <summary>
/// Stable permission keys. Since ADR-0003, built-in role -> permission grants live in the
/// database (tenancy.role_permissions, seeded from <see cref="BuiltInRoles"/>) so a workspace can
/// define custom roles later without changing call sites. <see cref="RolePermissions"/> below remains
/// a fast-path, no-DB-access fallback for a member with no resolved RoleId.
/// </summary>
public static class TenancyPermissions
{
    public const string WorkspaceManage = "workspace.manage";
    public const string MembersView = "members.view";
    public const string MembersInvite = "members.invite";
    public const string MembersManage = "members.manage";
    public const string RolesManage = "roles.manage";
    public const string FeaturesView = "features.view";

    // Baseline resource-action keys consumed by later changes (private spaces, task sharing/ACL).
    // Only seeds built-in role grants for these; per-resource enforcement is the resource ACL.
    public const string SpaceView = "space.view";
    public const string SpaceEdit = "space.edit";
    public const string SpaceManage = "space.manage";
    public const string TaskView = "task.view";
    public const string TaskComment = "task.comment";
    public const string TaskEdit = "task.edit";
    public const string TaskManage = "task.manage";
    public const string TaskShare = "task.share";
}

/// <summary>Fast-path (no DB access) permission lookup keyed by the compatibility MembershipRole enum.</summary>
public static class RolePermissions
{
    public static readonly IReadOnlySet<string> None = new HashSet<string>();

    public static IReadOnlySet<string> For(MembershipRole role) => BuiltInRoles.For(role).Permissions;

    public static bool Allows(MembershipRole role, string permission) => For(role).Contains(permission);
}
