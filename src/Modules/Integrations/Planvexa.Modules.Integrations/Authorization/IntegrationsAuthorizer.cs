namespace Planvexa.Modules.Integrations.Authorization;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Integrations authorization. Managing webhooks (create/list/delete/inspect deliveries) is
/// administrative (Admin+). Personal access tokens are owned by the acting user — any member may manage
/// their own. Guests have no access.
/// </summary>
public static class IntegrationsAuthorizer
{
    public static bool CanRead(WorkspaceRole? role) => role is not null;

    public static bool CanUse(WorkspaceRole? role) => role >= WorkspaceRole.Member;

    public static bool CanManageWebhooks(WorkspaceRole? role) => role >= WorkspaceRole.Admin;

    /// <summary>OAuth applications and third-party provider settings are a new privilege boundary
    /// () — Admin+ only, same bar as webhooks.</summary>
    public static bool CanManageOAuthApps(WorkspaceRole? role) => role >= WorkspaceRole.Admin;

    public static bool CanManageProviderSettings(WorkspaceRole? role) => role >= WorkspaceRole.Admin;

    public static void EnsureManageWebhooks(WorkspaceRole? role)
    {
        if (!CanRead(role))
        {
            throw new ForbiddenException("You do not have access to this workspace.");
        }

        if (!CanManageWebhooks(role))
        {
            throw new ForbiddenException("Administrator access is required to manage webhooks.");
        }
    }

    public static void EnsureManageOAuthApps(WorkspaceRole? role)
    {
        if (!CanRead(role))
        {
            throw new ForbiddenException("You do not have access to this workspace.");
        }

        if (!CanManageOAuthApps(role))
        {
            throw new ForbiddenException("Administrator access is required to manage OAuth applications.");
        }
    }

    public static void EnsureManageProviderSettings(WorkspaceRole? role)
    {
        if (!CanRead(role))
        {
            throw new ForbiddenException("You do not have access to this workspace.");
        }

        if (!CanManageProviderSettings(role))
        {
            throw new ForbiddenException("Administrator access is required to manage integration settings.");
        }
    }

    public static void EnsureMember(WorkspaceRole? role)
    {
        if (!CanRead(role))
        {
            throw new ForbiddenException("You do not have access to this workspace.");
        }

        if (!CanUse(role))
        {
            throw new ForbiddenException("Guests cannot manage personal access tokens.");
        }
    }
}
