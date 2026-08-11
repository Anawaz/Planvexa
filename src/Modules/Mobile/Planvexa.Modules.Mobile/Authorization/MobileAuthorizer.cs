namespace Planvexa.Modules.Mobile.Authorization;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Mobile authorization. Device management and mobile sync require normal workspace membership.
/// Guests may see that a workspace exists but cannot use mobile device features.
/// </summary>
public static class MobileAuthorizer
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
            throw new ForbiddenException("Guests cannot use mobile device features.");
        }
    }
}
