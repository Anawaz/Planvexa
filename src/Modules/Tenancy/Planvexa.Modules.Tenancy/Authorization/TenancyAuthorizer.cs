namespace Planvexa.Modules.Tenancy.Authorization;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Tenancy.Domain;

/// <summary>Central authorization decision point for tenancy operations.</summary>
public static class TenancyAuthorizer
{
    public const string WorkspaceManagePermission = "workspace.manage";
    public const string MembersViewPermission = "members.view";
    public const string MembersManagePermission = "members.manage";
    public const string MembersInvitePermission = "members.invite";

    /// <summary>Fast-path check against the compatibility MembershipRole enum (no DB access).</summary>
    public static bool Can(MembershipRole? role, string permission)
        => role is not null && RolePermissions.Allows(role.Value, permission);

    /// <summary>Throws <see cref="ForbiddenException"/> when the role lacks the permission.</summary>
    public static void Ensure(MembershipRole? role, string permission)
    {
        if (!Can(role, permission))
        {
            throw new ForbiddenException($"The current role is not permitted to '{permission}'.");
        }
    }

    /// <summary>DB-backed check against an already-resolved permission set (see IRolePermissionResolver).</summary>
    public static bool Can(IReadOnlySet<string> permissions, string permission) => permissions.Contains(permission);

    /// <summary>Throws <see cref="ForbiddenException"/> when the resolved permission set lacks the permission.</summary>
    public static void Ensure(IReadOnlySet<string> permissions, string permission)
    {
        if (!Can(permissions, permission))
        {
            throw new ForbiddenException($"The current role is not permitted to '{permission}'.");
        }
    }
}
