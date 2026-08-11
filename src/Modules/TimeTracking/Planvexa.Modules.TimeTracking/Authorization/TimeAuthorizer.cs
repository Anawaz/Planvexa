namespace Planvexa.Modules.TimeTracking.Authorization;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Time-tracking authorization. Members track and manage their OWN time; Admin+ can edit others'
/// entries, manage policy/rates, and approve/lock timesheets. Guests have no time-tracking access.
/// </summary>
public static class TimeAuthorizer
{
    public static bool CanTrackOwn(WorkspaceRole? role) => role >= WorkspaceRole.Member;

    public static bool CanManage(WorkspaceRole? role) => role >= WorkspaceRole.Admin;

    public static void EnsureTrackOwn(WorkspaceRole? role)
    {
        if (!CanTrackOwn(role))
        {
            throw new ForbiddenException("You do not have permission to track time in this workspace.");
        }
    }

    public static void EnsureManage(WorkspaceRole? role)
    {
        if (!CanManage(role))
        {
            throw new ForbiddenException("Administrator access is required for this time-tracking operation.");
        }
    }

    /// <summary>A member may act on their own entry; an admin may act on anyone's.</summary>
    public static void EnsureCanActOnEntry(WorkspaceRole? role, Guid entryOwnerUserId, Guid callerUserId)
    {
        if (role is null)
        {
            throw new ForbiddenException("You do not have access to this workspace.");
        }

        if (entryOwnerUserId == callerUserId)
        {
            EnsureTrackOwn(role);
            return;
        }

        EnsureManage(role);
    }
}
