namespace Planvexa.Modules.Forms.Authorization;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Forms authorization. Authoring forms (create/edit/delete) and reading submissions require Member+.
/// Guests are read-only. Public submission is anonymous and authorized by the form's public token, not
/// by workspace role.
/// </summary>
public static class FormsAuthorizer
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
            throw new ForbiddenException("Guests cannot author forms.");
        }
    }
}
