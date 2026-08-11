namespace Planvexa.Modules.Planning.Authorization;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Planning authorization. Calendar configuration (work schedule, holidays), sprints and workload
/// reporting are administrative (Admin+). Task estimates are content (Member+). A member may record
/// their own leave; recording leave for another user requires Admin+. Guests are read-only.
/// </summary>
public static class PlanningAuthorizer
{
    public static bool CanRead(WorkspaceRole? role) => role is not null;

    public static bool CanEditContent(WorkspaceRole? role) => role >= WorkspaceRole.Member;

    public static bool CanManage(WorkspaceRole? role) => role >= WorkspaceRole.Admin;

    public static void EnsureRead(WorkspaceRole? role)
    {
        if (!CanRead(role))
        {
            throw new ForbiddenException("You do not have access to this workspace.");
        }
    }

    public static void EnsureEditContent(WorkspaceRole? role)
    {
        EnsureRead(role);
        if (!CanEditContent(role))
        {
            throw new ForbiddenException("Guests cannot modify planning data in this workspace.");
        }
    }

    public static void EnsureManage(WorkspaceRole? role)
    {
        EnsureRead(role);
        if (!CanManage(role))
        {
            throw new ForbiddenException("Administrator access is required for this planning operation.");
        }
    }
}
