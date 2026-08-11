namespace Planvexa.Api.Realtime;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Planvexa.Api.Auth;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Chat.Application;
using Planvexa.Modules.Chat.Application.Services;
using Planvexa.Modules.Tenancy.Application;
using Planvexa.SharedContracts.Users;
using Planvexa.SharedContracts.Workspaces;

/// <summary>Stable realtime group names.</summary>
public static class RealtimeGroups
{
    public static string Workspace(Guid workspaceId) => workspaceId.ToString("N");

    /// <summary>Per-user group every connection joins alongside its workspace group, so a server-side
    /// check can target one recipient without a distributed user-id mapping (see WorkspaceHub.Typing).</summary>
    public static string User(Guid userId) => $"user:{userId:N}";
}

/// <summary>
/// Realtime hub. A hub-method invocation runs in its own DI scope (distinct from the HTTP middleware
/// scope), so the hub resolves the caller's identity from <see cref="HubCallerContext.User"/> and then
/// resolves the ambient Workspace from the workspace being joined. Clients join a workspace group only
/// after their access is verified via <see cref="IWorkspaceAccessQuery"/> — the server never places a
/// connection in a group the caller cannot access. The database remains authoritative; hub messages only
/// signal that data changed.
/// </summary>
[Authorize]
public sealed class WorkspaceHub(
    IWorkspaceContextAccessor workspaceAccessor,
    CurrentUser currentUser,
    IUserDirectory users,
    IWorkspaceResolver workspaceResolver,
    IWorkspaceAccessQuery access,
    PresenceTracker presence,
    ChatChannelService chatChannelService) : Hub
{
    /// <summary>How long a "typing" broadcast stays valid client-side before it auto-expires.</summary>
    private static readonly TimeSpan TypingTtl = TimeSpan.FromSeconds(6);

    public async Task JoinWorkspace(Guid workspaceId)
    {
        var userId = await ResolveContextAsync(workspaceId);

        var group = RealtimeGroups.Workspace(workspaceId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
        // Also join a personal group so Typing can target one recipient directly (see its doc comment).
        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.User(userId), Context.ConnectionAborted);
        Context.Items[$"group:{workspaceId:N}"] = group;
        Context.Items["userId"] = userId;

        presence.Add(group, userId);
        await Clients.Group(group).SendAsync("presence", new { workspaceId, userIds = presence.UsersIn(group) }, Context.ConnectionAborted);
    }

    /// <summary>
    /// Ephemeral, non-persisted "user X is typing in resource Y" signal, broadcast to the same workspace
    /// group presence already uses (no per-resource SignalR group exists — see RealtimeGroups' doc
    /// comment — so clients filter the broadcast by resourceType/resourceId themselves, exactly like they
    /// already filter "entityChanged"/"presence" by workspaceId). Best-effort: a caller that has not
    /// joined this workspace's group is silently ignored rather than throwing, since a stray typing ping
    /// racing a tab close/switch is expected, not an error. No server-side state is kept — a dropped
    /// connection or an idle client simply stops refreshing the signal, and every recipient independently
    /// expires it client-side at <c>expiresAtUtc</c>, so a lost "stopped typing" message never leaves a
    /// stale indicator.
    /// </summary>
    public async Task Typing(Guid workspaceId, string resourceType, Guid resourceId)
    {
        if (!Context.Items.TryGetValue($"group:{workspaceId:N}", out var groupObj) || groupObj is not string group
            || !Context.Items.TryGetValue("userId", out var userObj) || userObj is not Guid userId)
        {
            return;
        }

        var payload = new
        {
            workspaceId,
            resourceType,
            resourceId,
            userId,
            expiresAtUtc = DateTimeOffset.UtcNow.Add(TypingTtl),
        };

        // A ChatChannel may be Private/Dm/GroupDm, in which case broadcasting to the whole workspace
        // group (as every other resourceType safely does) would leak the existence/activity of a
        // conversation to workspace members who cannot see it. Verify the caller can read the channel,
        // and if it's membership-gated, relay only to its members instead of the whole workspace.
        if (string.Equals(resourceType, "ChatChannel", StringComparison.Ordinal))
        {
            // Hub method invocations are their own DI scope (see class doc); rehydrate just enough of
            // it for ChatChannelService's access check (it reads UserId/WorkspaceId, nothing else here).
            currentUser.Set(userId, userId.ToString(), string.Empty, string.Empty);
            workspaceAccessor.Set(new WorkspaceContext(
                workspaceId, userId, null, string.Empty, new HashSet<string>(), new HashSet<string>(), Guid.CreateVersion7().ToString()));

            ChatChannelDto channel;
            try
            {
                channel = await chatChannelService.GetAsync(resourceId, Context.ConnectionAborted);
            }
            catch (Exception ex) when (ex is ForbiddenException or NotFoundException)
            {
                // No access (or it doesn't exist/isn't in this workspace) — no-op rather than leaking
                // its existence via an error, exactly like a dropped/racing typing ping.
                return;
            }

            if (channel.IsPrivate)
            {
                var recipients = channel.MemberUserIds.Where(id => id != userId)
                    .Select(id => Clients.Group(RealtimeGroups.User(id)).SendAsync("typing", payload, Context.ConnectionAborted));
                await Task.WhenAll(recipients);
                return;
            }
        }

        await Clients.OthersInGroup(group).SendAsync("typing", payload, Context.ConnectionAborted);
    }

    public async Task LeaveWorkspace(Guid workspaceId)
    {
        if (Context.Items.TryGetValue($"group:{workspaceId:N}", out var groupObj) && groupObj is string group
            && Context.Items.TryGetValue("userId", out var userObj) && userObj is Guid userId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.User(userId), Context.ConnectionAborted);
            if (presence.Remove(group, userId))
            {
                await Clients.Group(group).SendAsync("presence", new { workspaceId, userIds = presence.UsersIn(group) }, Context.ConnectionAborted);
            }
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("userId", out var userObj) && userObj is Guid userId)
        {
            foreach (var kvp in Context.Items)
            {
                if (kvp.Key is string key && key.StartsWith("group:", StringComparison.Ordinal) && kvp.Value is string group
                    && presence.Remove(group, userId))
                {
                    await Clients.Group(group).SendAsync("presence", new { group, userIds = presence.UsersIn(group) });
                }
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task<Guid> ResolveContextAsync(Guid workspaceId)
    {
        var principal = Context.User ?? throw new HubException("Not authenticated.");
        var subject = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new HubException("Not authenticated.");
        var email = principal.FindFirstValue("email") ?? principal.FindFirstValue(ClaimTypes.Email) ?? $"{subject}@unknown.local";
        var name = principal.FindFirstValue("name") ?? principal.FindFirstValue(ClaimTypes.Name) ?? email;

        var user = await users.GetOrProvisionAsync(subject, email, name, Context.ConnectionAborted);

        // A hub invocation gets its own DI scope and never runs UserContextMiddleware, so the scoped
        // CurrentUser is unset here. Workspace resolution reads the caller's own memberships through
        // the user-scoped bootstrap RLS policies, which need app.current_user — populate it first.
        currentUser.Set(user.UserId, subject, user.Email, user.DisplayName, AmrClaims.HasVerifiedMfa(principal));

        var resolution = await workspaceResolver.ResolveByWorkspaceIdAsync(workspaceId, user.UserId, Context.ConnectionAborted)
            ?? throw new HubException("You do not have access to this workspace.");

        // Same MFA gate as WorkspaceResolutionMiddleware (HTTP) — a hub connection is a second,
        // independent way into a Workspace's realtime events and must not bypass the policy.
        if (resolution.RequiresMfa && !currentUser.HasVerifiedMfa)
        {
            throw new HubException("This workspace requires multi-factor authentication.");
        }

        // Set the workspace for THIS invocation scope so the access query is correctly scoped.
        workspaceAccessor.Set(new WorkspaceContext(
            resolution.WorkspaceId, user.UserId, null, resolution.Role.ToString(),
            new HashSet<string>(), resolution.EnabledFeatures, Guid.CreateVersion7().ToString()));

        var callerAccess = await access.GetAccessAsync(workspaceId, user.UserId, Context.ConnectionAborted)
            ?? throw new HubException("You do not have access to this workspace.");
        _ = callerAccess;

        return user.UserId;
    }
}
