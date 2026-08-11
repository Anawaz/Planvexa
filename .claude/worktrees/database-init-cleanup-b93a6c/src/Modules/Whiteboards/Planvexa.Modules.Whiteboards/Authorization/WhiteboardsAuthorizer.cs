namespace Planvexa.Modules.Whiteboards.Authorization;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Whiteboards authorization. Mirrors DocumentsAuthorizer exactly: workspace members may create/edit
/// shared whiteboards; guests are read-only; private whiteboards are further gated on the aggregate
/// itself (owner-only) and linked whiteboards on the linked resource's ACL — both resolved by
/// WhiteboardService, not here (this is only the coarse workspace-role floor).
/// </summary>
public static class WhiteboardsAuthorizer
{
    public static bool CanRead(WorkspaceRole? role) => role is not null;

    public static bool CanEdit(WorkspaceRole? role) => role >= WorkspaceRole.Member;

    public static bool CanManage(WorkspaceRole? role) => role >= WorkspaceRole.Admin;

    public static void EnsureRead(WorkspaceRole? role)
    {
        if (!CanRead(role))
        {
            throw new ForbiddenException("You do not have access to this workspace.");
        }
    }

    public static void EnsureEdit(WorkspaceRole? role)
    {
        EnsureRead(role);
        if (!CanEdit(role))
        {
            throw new ForbiddenException("Guests cannot modify whiteboards in this workspace.");
        }
    }

    public static void EnsureManage(WorkspaceRole? role)
    {
        EnsureRead(role);
        if (!CanManage(role))
        {
            throw new ForbiddenException("Administrator access is required for this whiteboards operation.");
        }
    }
}
