namespace Planvexa.Modules.Chat.Application;

using Planvexa.Modules.Chat.Domain;

// ---- DTOs ----
public sealed record ChatChannelDto(
    Guid Id, ChatChannelType ChannelType, string Name, string? Description, bool IsPrivate, bool IsArchived,
    string? LinkedResourceType, Guid? LinkedResourceId, Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc, IReadOnlyList<Guid> MemberUserIds);

public sealed record ChatChannelSummaryDto(
    Guid Id, ChatChannelType ChannelType, string Name, string? Description, bool IsPrivate, bool IsArchived,
    string? LinkedResourceType, Guid? LinkedResourceId, DateTimeOffset CreatedAtUtc,
    IReadOnlyList<Guid> MemberUserIds, int UnreadCount);

public sealed record ChatReactionDto(string Emoji, IReadOnlyList<Guid> UserIds);

public sealed record ChatAttachmentDto(
    Guid Id, Guid MessageId, string FileName, string ContentType, long SizeBytes, Guid UploadedByUserId, DateTimeOffset CreatedAtUtc);

public sealed record ChatMessageDto(
    Guid Id, Guid ChannelId, Guid? ParentMessageId, Guid AuthorUserId, string Body, bool IsDeleted,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? EditedAtUtc,
    IReadOnlyList<Guid> MentionUserIds, IReadOnlyList<ChatReactionDto> Reactions, IReadOnlyList<ChatAttachmentDto> Attachments);

// ---- Commands ----
public sealed record CreateChannelCommand(string Name, string? Description, bool IsPrivate, IReadOnlyList<Guid>? MemberUserIds);

/// <summary>Creates a channel linked to a Space/List/Task; access is gated by the linked resource's ACL
/// (see ChatChannel's doc comment) in addition to the workspace-role floor.</summary>
public sealed record CreateLinkedChannelCommand(string LinkedResourceType, Guid LinkedResourceId, string Name, string? Description);

/// <summary>Starts (or reuses, if one already exists) a DM/group DM with the given other participants.
/// The caller is always included automatically. Exactly 1 other participant => Dm; 2+ => GroupDm.</summary>
public sealed record CreateDirectMessageCommand(IReadOnlyList<Guid> ParticipantUserIds);

public sealed record UpdateChannelCommand(string? Name, string? Description);

public sealed record PostMessageCommand(Guid ChannelId, Guid? ParentMessageId, string Body, IReadOnlyList<Guid>? MentionUserIds);

public sealed record EditMessageCommand(string Body);

public sealed record MarkChannelReadCommand(Guid? LastReadMessageId);
