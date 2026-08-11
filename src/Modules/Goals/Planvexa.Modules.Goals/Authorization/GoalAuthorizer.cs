namespace Planvexa.Modules.Goals.Authorization;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.SharedContracts.Workspaces;

/// <summary>Goals authorization: any Member+ may create/edit/comment; Guests are read-only. Mirrors
/// ReportingAuthorizer/PlanningAuthorizer's coarse-role gate — Goals has no per-resource ACL/private-goal
/// concept (workspace-wide OKRs are visible workspace-wide by design).</summary>
public static class GoalAuthorizer
{
    public static bool CanRead(WorkspaceRole? role) => role is not null;

    public static bool CanEdit(WorkspaceRole? role) => role >= WorkspaceRole.Member;

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
            throw new ForbiddenException("Guests cannot create or modify goals.");
        }
    }
}
