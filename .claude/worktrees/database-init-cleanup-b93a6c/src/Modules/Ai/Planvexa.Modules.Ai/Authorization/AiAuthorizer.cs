namespace Planvexa.Modules.Ai.Authorization;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// AI authorization. Using AI assistance requires workspace Member+ (guests are read-only and cannot use
/// AI).
/// </summary>
public static class AiAuthorizer
{
    public static bool CanRead(WorkspaceRole? role) => role is not null;

    public static bool CanUse(WorkspaceRole? role) => role >= WorkspaceRole.Member;

    public static void EnsureRead(WorkspaceRole? role)
    {
        if (!CanRead(role))
        {
            throw new ForbiddenException("You do not have access to this workspace.");
        }
    }

    public static void EnsureUse(WorkspaceRole? role)
    {
        EnsureRead(role);
        if (!CanUse(role))
        {
            throw new ForbiddenException("Guests cannot use AI assistance.");
        }
    }

    /// <summary>Reading or changing the tenant's AI provider settings requires Admin+.</summary>
    public static bool CanManage(WorkspaceRole? role) => role >= WorkspaceRole.Admin;

    public static void EnsureManage(WorkspaceRole? role)
    {
        EnsureRead(role);
        if (!CanManage(role))
        {
            throw new ForbiddenException("Only workspace administrators can manage AI provider settings.");
        }
    }
}
