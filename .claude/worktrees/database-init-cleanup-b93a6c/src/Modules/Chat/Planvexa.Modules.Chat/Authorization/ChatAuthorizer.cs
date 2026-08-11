namespace Planvexa.Modules.Chat.Authorization;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Chat authorization. Reading/posting requires workspace Member+ (guests are read-only and cannot chat).
/// Channel administration (rename, archive, manage private membership) requires the channel creator or a
/// workspace Admin+; the latter is also the message moderator (may delete any message). Channel-level
/// access (public vs private) is enforced on the aggregate.
/// </summary>
public static class ChatAuthorizer
{
    public static bool CanRead(WorkspaceRole? role) => role is not null;

    public static bool CanParticipate(WorkspaceRole? role) => role >= WorkspaceRole.Member;

    public static bool IsModerator(WorkspaceRole? role) => role >= WorkspaceRole.Admin;

    public static void EnsureRead(WorkspaceRole? role)
    {
        if (!CanRead(role))
        {
            throw new ForbiddenException("You do not have access to this workspace.");
        }
    }

    public static void EnsureParticipate(WorkspaceRole? role)
    {
        EnsureRead(role);
        if (!CanParticipate(role))
        {
            throw new ForbiddenException("Guests cannot participate in chat.");
        }
    }
}
