namespace Planvexa.Modules.Automations.Authorization;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Automations authorization. Managing rules (create/edit/enable/disable/delete) is administrative
/// (Admin+). Run history is readable by any workspace member. Guests have no access.
/// </summary>
public static class AutomationsAuthorizer
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
            throw new ForbiddenException("Administrator access is required to manage automations.");
        }
    }
}
