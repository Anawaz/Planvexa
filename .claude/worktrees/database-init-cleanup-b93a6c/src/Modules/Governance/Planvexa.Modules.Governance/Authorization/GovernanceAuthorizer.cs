namespace Planvexa.Modules.Governance.Authorization;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.SharedContracts.Workspaces;

/// <summary>Governance authorization. All governance operations require workspace Admin+ access.</summary>
public static class GovernanceAuthorizer
{
    public static bool CanRead(WorkspaceRole? role) => role is not null;

    public static bool CanManage(WorkspaceRole? role) => role >= WorkspaceRole.Admin;

    public static void EnsureRead(WorkspaceRole? role)
    {
        if (!CanRead(role))
        {
            throw new ForbiddenException("You do not have access to this workspace.");
        }
    }

    public static void EnsureManage(WorkspaceRole? role)
    {
        EnsureRead(role);
        if (!CanManage(role))
        {
            throw new ForbiddenException("Administrator access is required for governance operations.");
        }
    }
}

