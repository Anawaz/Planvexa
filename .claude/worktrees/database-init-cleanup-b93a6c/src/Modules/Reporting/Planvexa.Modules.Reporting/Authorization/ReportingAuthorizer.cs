namespace Planvexa.Modules.Reporting.Authorization;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Reporting authorization. Any member may create/edit dashboards; a private dashboard is visible
/// only to its owner (enforced on the aggregate). Portfolio and workload reports are administrative.
/// Guests are read-only.
/// </summary>
public static class ReportingAuthorizer
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
            throw new ForbiddenException("Guests cannot create or modify dashboards.");
        }
    }

    public static void EnsureManage(WorkspaceRole? role)
    {
        EnsureRead(role);
        if (!CanManage(role))
        {
            throw new ForbiddenException("Administrator access is required for this report.");
        }
    }
}
