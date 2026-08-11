namespace Planvexa.Modules.Chat.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Chat.Authorization;
using Planvexa.Modules.Chat.Domain;
using Planvexa.SharedContracts.Notifications;

/// <summary>Posts, edits, deletes and lists chat messages (with mentions/reactions/attachments),
/// broadcasting changes in realtime.</summary>
public sealed class ChatMessageService(
    ChatServiceContext ctx,
    IChatMessageStore messages,
    IChatAttachmentStore attachments,
    ChatChannelService channelService)
    : ChatServiceBase(ctx)
{
    public async Task<IReadOnlyList<ChatMessageDto>> ListAsync(Guid channelId, DateTimeOffset? beforeUtc, CancellationToken ct)
    {
        var (channel, _) = await channelService.LoadForReadAsync(channelId, ct);

        var list = await messages.ListByChannelAsync(channel.Id, beforeUtc, 100, ct);
        var attachmentsByMessage = (await attachments.ListForMessagesAsync(list.Select(m => m.Id).ToList(), ct))
            .ToLookup(a => a.MessageId);
        return list.Select(m => ToDto(m, attachmentsByMessage[m.Id].ToList())).ToList();
    }

    public async Task<ChatMessageDto> PostAsync(PostMessageCommand command, CancellationToken ct)
    {
        var (channel, role) = await channelService.LoadForReadAsync(command.ChannelId, ct);
        ChatAuthorizer.EnsureParticipate(role);

        if (channel.IsArchived)
        {
            throw new ConflictException("This channel is archived and no longer accepts messages.");
        }

        // A reply's parent must be a top-level message in the same channel (one level of threading).
        if (command.ParentMessageId is { } parentId)
        {
            var parent = await messages.FindAsync(parentId, ct)
                ?? throw new NotFoundException("Parent message not found.");
            if (parent.ChannelId != channel.Id)
            {
                throw new ValidationAppException("A reply must belong to the same channel as its parent.");
            }

            if (parent.ParentMessageId is not null)
            {
                throw new ValidationAppException("Replies can only be added to top-level messages.");
            }
        }

        // Mentions are validated against workspace membership (same rule as Collaboration's Comment
        // mentions) so a mention can never leak a notification to someone outside the workspace.
        var validMentions = await ValidateMentionsAsync(channel.WorkspaceId, command.MentionUserIds, ct);

        var message = ChatMessage.Create(
            NewId(), channel.WorkspaceId, channel.Id, command.ParentMessageId, UserId, command.Body, Now,
            validMentions, NewId);
        messages.Add(message);
        Audit("chat.message.posted", "ChatMessage", message.Id, new { channel.Id });

        foreach (var mentionedUserId in validMentions.Where(u => u != UserId))
        {
            await Ctx.Notifications.PublishAsync(new NotificationRequest(
                RecipientUserId: mentionedUserId,
                EventType: "mention",
                EntityType: "ChatMessage",
                EntityId: message.Id,
                WorkspaceId: channel.WorkspaceId,
                DeduplicationKey: $"chat-mention:{message.Id:N}:{mentionedUserId:N}",
                Payload: new Dictionary<string, string>
                {
                    ["channelId"] = channel.Id.ToString(),
                    ["channelName"] = channel.Name,
                    ["byUserId"] = UserId.ToString(),
                }), ct);
        }

        await SaveAsync(ct);
        await NotifyAsync(channel.WorkspaceId, "ChatMessage", message.Id, "posted", ct);
        return ToDto(message, []);
    }

    public async Task<ChatMessageDto> EditAsync(Guid messageId, EditMessageCommand command, CancellationToken ct)
    {
        var message = await messages.FindWithChildrenAsync(messageId, ct)
            ?? throw new NotFoundException("Message not found.");

        // Confirm the caller can access the channel the message belongs to.
        var (channel, _) = await channelService.LoadForReadAsync(message.ChannelId, ct);

        message.Edit(command.Body, UserId, Now);
        Audit("chat.message.edited", "ChatMessage", message.Id);
        await SaveAsync(ct);
        await NotifyAsync(channel.WorkspaceId, "ChatMessage", message.Id, "edited", ct);
        return ToDto(message, await attachments.ListForMessageAsync(message.Id, ct));
    }

    public async Task DeleteAsync(Guid messageId, CancellationToken ct)
    {
        var message = await messages.FindAsync(messageId, ct)
            ?? throw new NotFoundException("Message not found.");

        var (channel, role) = await channelService.LoadForReadAsync(message.ChannelId, ct);

        message.Delete(UserId, ChatAuthorizer.IsModerator(role), Now);
        Audit("chat.message.deleted", "ChatMessage", message.Id);
        await SaveAsync(ct);
        await NotifyAsync(channel.WorkspaceId, "ChatMessage", message.Id, "deleted", ct);
    }

    public async Task<ChatMessageDto> AddReactionAsync(Guid messageId, string emoji, CancellationToken ct)
    {
        var (message, channel) = await LoadForReactAsync(messageId, ct);
        if (message.AddReaction(NewId(), UserId, emoji))
        {
            await SaveAsync(ct);
            await NotifyAsync(channel.WorkspaceId, "ChatMessage", message.Id, "reacted", ct);
        }

        return ToDto(message, await attachments.ListForMessageAsync(message.Id, ct));
    }

    public async Task<ChatMessageDto> RemoveReactionAsync(Guid messageId, string emoji, CancellationToken ct)
    {
        var (message, channel) = await LoadForReactAsync(messageId, ct);
        if (message.RemoveReaction(UserId, emoji))
        {
            await SaveAsync(ct);
            await NotifyAsync(channel.WorkspaceId, "ChatMessage", message.Id, "reacted", ct);
        }

        return ToDto(message, await attachments.ListForMessageAsync(message.Id, ct));
    }

    private async Task<(ChatMessage Message, ChatChannel Channel)> LoadForReactAsync(Guid messageId, CancellationToken ct)
    {
        var message = await messages.FindWithChildrenAsync(messageId, ct)
            ?? throw new NotFoundException("Message not found.");
        var (channel, role) = await channelService.LoadForReadAsync(message.ChannelId, ct);
        ChatAuthorizer.EnsureParticipate(role);
        return (message, channel);
    }

    private async Task<IReadOnlyList<Guid>> ValidateMentionsAsync(Guid workspaceId, IReadOnlyList<Guid>? mentionUserIds, CancellationToken ct)
    {
        if (mentionUserIds is null || mentionUserIds.Count == 0)
        {
            return [];
        }

        var valid = new List<Guid>();
        foreach (var userId in mentionUserIds.Distinct())
        {
            if (await Ctx.Access.GetAccessAsync(workspaceId, userId, ct) is not null)
            {
                valid.Add(userId);
            }
        }

        return valid;
    }

    private static ChatMessageDto ToDto(ChatMessage m, IReadOnlyList<ChatAttachment> messageAttachments) => new(
        m.Id, m.ChannelId, m.ParentMessageId, m.AuthorUserId, m.Body, m.IsDeleted, m.CreatedAtUtc, m.EditedAtUtc,
        m.Mentions.Select(x => x.MentionedUserId).ToList(),
        m.Reactions.GroupBy(r => r.Emoji).Select(g => new ChatReactionDto(g.Key, g.Select(r => r.UserId).ToList())).ToList(),
        messageAttachments.Select(a => new ChatAttachmentDto(a.Id, a.MessageId, a.FileName, a.ContentType, a.SizeBytes, a.UploadedByUserId, a.CreatedAtUtc)).ToList());
}
