namespace Planvexa.Modules.Chat.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Chat.Authorization;
using Planvexa.Modules.Chat.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>Manages chat channels (workspace/private/linked/DM) and their membership.</summary>
public sealed class ChatChannelService(ChatServiceContext ctx, IChatChannelStore channels, IChatMessageStore messages, IChatChannelReadStateStore readStates)
    : ChatServiceBase(ctx)
{
    public async Task<IReadOnlyList<ChatChannelSummaryDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        ChatAuthorizer.EnsureRead(role);

        var list = await channels.ListByWorkspaceAsync(workspaceId, ct);
        var readState = (await readStates.ListForUserAsync(workspaceId, UserId, ct)).ToDictionary(s => s.ChannelId);

        var result = new List<ChatChannelSummaryDto>();
        foreach (var channel in list)
        {
            if (!await CanAccessAsync(channel, role, ct))
            {
                continue;
            }

            var lastReadAt = readState.TryGetValue(channel.Id, out var state) ? state.LastReadAtUtc : (DateTimeOffset?)null;
            var unread = await messages.CountAfterAsync(channel.Id, lastReadAt, ct);
            result.Add(ToSummary(channel, unread));
        }

        return result;
    }

    public async Task<ChatChannelDto> CreateAsync(CreateChannelCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ChatAuthorizer.EnsureParticipate((await AccessAsync(workspaceId, ct))?.Role);

        var channel = ChatChannel.Create(NewId(), workspaceId, command.Name, command.Description, command.IsPrivate, UserId, NewId, Now);
        if (command.IsPrivate && command.MemberUserIds is { Count: > 0 } members)
        {
            foreach (var memberId in members.Where(m => m != UserId))
            {
                channel.AddMember(NewId(), memberId, Now);
            }
        }

        channels.Add(channel);
        Audit("chat.channel.created", "ChatChannel", channel.Id, new { channel.Name, channel.IsPrivate });
        await SaveAsync(ct);
        await NotifyAsync(workspaceId, "ChatChannel", channel.Id, "created", ct);
        return ToDto(channel);
    }

    /// <summary>Creates a channel linked to a Space/List/Task. The caller must currently be able to read
    /// the linked resource (else linking would let them create a channel exposing a resource they cannot
    /// themselves see) — checked via the same ACL resolver <see cref="CanAccessAsync"/> uses for reads.</summary>
    public async Task<ChatChannelDto> CreateLinkedAsync(CreateLinkedChannelCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        ChatAuthorizer.EnsureParticipate(role);

        if (command.LinkedResourceType is not (ChatLinkedResourceTypes.Space or ChatLinkedResourceTypes.List or ChatLinkedResourceTypes.Task))
        {
            throw new ValidationAppException("linkedResourceType must be space, list, or task.");
        }

        var level = await Ctx.ResourcePermissions.GetEffectiveAsync(workspaceId, UserId, command.LinkedResourceType, command.LinkedResourceId, ct);
        if (level is null || level < PermissionLevel.View)
        {
            throw new ForbiddenException("You do not have access to the resource this channel would be linked to.");
        }

        var type = command.LinkedResourceType switch
        {
            ChatLinkedResourceTypes.Space => ChatChannelType.Space,
            ChatLinkedResourceTypes.List => ChatChannelType.List,
            _ => ChatChannelType.Task,
        };

        var channel = ChatChannel.CreateLinked(
            NewId(), workspaceId, type, command.Name, command.Description,
            command.LinkedResourceType, command.LinkedResourceId, UserId, NewId, Now);

        channels.Add(channel);
        Audit("chat.channel.created", "ChatChannel", channel.Id, new { channel.Name, command.LinkedResourceType, command.LinkedResourceId });
        await SaveAsync(ct);
        await NotifyAsync(workspaceId, "ChatChannel", channel.Id, "created", ct);
        return ToDto(channel);
    }

    /// <summary>Starts (or reuses) a DM/group DM. Access is strictly membership-only — see ChatChannel's
    /// doc comment — so every participant must already be a workspace member (not necessarily Member+;
    /// even a Guest may be DM'd) but no workspace-role floor grants access beyond the explicit member set.</summary>
    public async Task<ChatChannelDto> CreateDirectMessageAsync(CreateDirectMessageCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ChatAuthorizer.EnsureParticipate((await AccessAsync(workspaceId, ct))?.Role);

        var participants = new List<Guid> { UserId };
        foreach (var userId in (command.ParticipantUserIds ?? []).Distinct())
        {
            if (userId == UserId)
            {
                continue;
            }

            if (await Ctx.Access.GetAccessAsync(workspaceId, userId, ct) is null)
            {
                throw new ValidationAppException("Every participant must be a member of this workspace.");
            }

            participants.Add(userId);
        }

        if (participants.Count < 2)
        {
            throw new ValidationAppException("A direct message needs at least one other participant.");
        }

        var type = participants.Count == 2 ? ChatChannelType.Dm : ChatChannelType.GroupDm;

        var existing = await channels.FindDirectMessageAsync(workspaceId, type, participants, ct);
        if (existing is not null)
        {
            return ToDto(existing);
        }

        var channel = ChatChannel.CreateDirect(NewId(), workspaceId, type, participants, UserId, NewId, Now);
        channels.Add(channel);
        Audit("chat.channel.created", "ChatChannel", channel.Id, new { channel.ChannelType, participants });
        await SaveAsync(ct);
        await NotifyAsync(workspaceId, "ChatChannel", channel.Id, "created", ct);
        return ToDto(channel);
    }

    public async Task<ChatChannelDto> GetAsync(Guid id, CancellationToken ct)
    {
        var (channel, _) = await LoadForReadAsync(id, ct);
        return ToDto(channel);
    }

    public async Task<ChatChannelDto> UpdateAsync(Guid id, UpdateChannelCommand command, CancellationToken ct)
    {
        var (channel, role) = await LoadForManageAsync(id, ct);
        channel.UpdateDetails(command.Name, command.Description, Now);
        Audit("chat.channel.updated", "ChatChannel", channel.Id, new { channel.Name });
        await SaveAsync(ct);
        await NotifyAsync(channel.WorkspaceId, "ChatChannel", channel.Id, "updated", ct);
        _ = role;
        return ToDto(channel);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        var (channel, _) = await LoadForManageAsync(id, ct);
        channel.Archive(Now);
        Audit("chat.channel.archived", "ChatChannel", channel.Id);
        await SaveAsync(ct);
        await NotifyAsync(channel.WorkspaceId, "ChatChannel", channel.Id, "archived", ct);
    }

    public async Task<ChatChannelDto> AddMemberAsync(Guid id, Guid memberUserId, CancellationToken ct)
    {
        var (channel, _) = await LoadForManageAsync(id, ct);
        channel.AddMember(NewId(), memberUserId, Now);
        Audit("chat.channel.member_added", "ChatChannel", channel.Id, new { memberUserId });
        await SaveAsync(ct);
        return ToDto(channel);
    }

    public async Task<ChatChannelDto> RemoveMemberAsync(Guid id, Guid memberUserId, CancellationToken ct)
    {
        var (channel, _) = await LoadForManageAsync(id, ct);
        if (!channel.RemoveMember(memberUserId))
        {
            throw new NotFoundException("The user is not a member of this channel.");
        }

        Audit("chat.channel.member_removed", "ChatChannel", channel.Id, new { memberUserId });
        await SaveAsync(ct);
        return ToDto(channel);
    }

    public async Task MarkReadAsync(Guid id, MarkChannelReadCommand command, CancellationToken ct)
    {
        var (channel, _) = await LoadForReadAsync(id, ct);
        var state = await readStates.FindAsync(channel.Id, UserId, ct);
        if (state is null)
        {
            state = new ChatChannelReadState(NewId(), channel.WorkspaceId, channel.Id, UserId, command.LastReadMessageId, Now);
            readStates.Add(state);
        }
        else
        {
            state.MarkRead(command.LastReadMessageId, Now);
        }

        await SaveAsync(ct);
    }

    /// <summary>Loads a channel the caller may read; throws Forbidden/NotFound otherwise. Returns the role too.</summary>
    internal async Task<(ChatChannel Channel, WorkspaceRole? Role)> LoadForReadAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        ChatAuthorizer.EnsureRead(role);

        var channel = await channels.FindWithMembersAsync(id, ct)
            ?? throw new NotFoundException("Channel not found.");
        if (channel.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Channel not found in this workspace.");
        }

        if (!await CanAccessAsync(channel, role, ct))
        {
            throw new ForbiddenException("You do not have access to this channel.");
        }

        return (channel, role);
    }

    /// <summary>
    /// Effective read access for a channel: the structural (membership/workspace) check on the aggregate
    /// itself, ANDed with the linked resource's ACL when the channel is Space/List/Task-linked (this is
    /// the security-critical wiring for the roadmap's channel-scoping item — a linked channel on
    /// a private List must be exactly as hidden as the List itself). The ACL check requires an async
    /// cross-module call (<see cref="Planvexa.SharedContracts.Workspaces.IResourcePermissionQuery"/>), so
    /// it cannot live on the domain entity (which stays synchronous/pure) — this is the one place it
    /// happens, reused by every read path (ListAsync, LoadForReadAsync, and ChatSearchProvider via
    /// <see cref="FilterAccessibleAsync"/>).
    /// </summary>
    internal async Task<bool> CanAccessAsync(ChatChannel channel, WorkspaceRole? role, CancellationToken ct)
    {
        if (!channel.CanBeAccessedBy(UserId, role >= WorkspaceRole.Member))
        {
            return false;
        }

        if (channel.LinkedResourceType is null || channel.LinkedResourceId is null)
        {
            return true;
        }

        var level = await Ctx.ResourcePermissions.GetEffectiveAsync(
            channel.WorkspaceId, UserId, channel.LinkedResourceType, channel.LinkedResourceId.Value, ct);
        return level is not null && level >= PermissionLevel.View;
    }

    /// <summary>Used by ChatSearchProvider so the exact same access rule governs both browsing and search
    /// results (the search permission-filtering guarantee, re-verified for the new channel
    /// types).</summary>
    internal async Task<IReadOnlyList<ChatChannel>> FilterAccessibleAsync(IReadOnlyList<ChatChannel> candidates, WorkspaceRole? role, CancellationToken ct)
    {
        var accessible = new List<ChatChannel>();
        foreach (var channel in candidates)
        {
            if (await CanAccessAsync(channel, role, ct))
            {
                accessible.Add(channel);
            }
        }

        return accessible;
    }

    private async Task<(ChatChannel Channel, WorkspaceRole? Role)> LoadForManageAsync(Guid id, CancellationToken ct)
    {
        var (channel, role) = await LoadForReadAsync(id, ct);
        if (channel.CreatedByUserId != UserId && !ChatAuthorizer.IsModerator(role))
        {
            throw new ForbiddenException("Only the channel creator or a workspace administrator can manage this channel.");
        }

        return (channel, role);
    }

    private static ChatChannelDto ToDto(ChatChannel c)
        => new(c.Id, c.ChannelType, c.Name, c.Description, c.IsPrivate, c.IsArchived, c.LinkedResourceType, c.LinkedResourceId,
            c.CreatedByUserId, c.CreatedAtUtc, c.Members.Select(m => m.UserId).ToList());

    private static ChatChannelSummaryDto ToSummary(ChatChannel c, int unreadCount)
        => new(c.Id, c.ChannelType, c.Name, c.Description, c.IsPrivate, c.IsArchived, c.LinkedResourceType, c.LinkedResourceId,
            c.CreatedAtUtc, c.Members.Select(m => m.UserId).ToList(), unreadCount);
}
