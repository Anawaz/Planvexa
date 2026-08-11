namespace Planvexa.Modules.Chat.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// A message posted to a chat channel. Supports one level of threading (a reply references a top-level
/// message), author edits, and soft deletion (body is cleared; the row remains for thread integrity).
/// Mentions are stored as child rows referencing validated workspace members (same pattern as
/// Collaboration's Comment.Mentions); reactions follow Comment.Reactions' exact shape.
/// </summary>
public sealed class ChatMessage : Entity, IAggregateRoot, IWorkspaceOwned
{
    private const int MaxBodyLength = 4000;

    private readonly List<ChatMention> _mentions = new();
    private readonly List<ChatMessageReaction> _reactions = new();

    private ChatMessage()
    {
    }

    private ChatMessage(Guid id, Guid workspaceId, Guid channelId, Guid? parentMessageId, Guid authorUserId, string body, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        ChannelId = channelId;
        ParentMessageId = parentMessageId;
        AuthorUserId = authorUserId;
        Body = body;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid ChannelId { get; private set; }
    public Guid? ParentMessageId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? EditedAtUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public IReadOnlyList<ChatMention> Mentions => _mentions.AsReadOnly();
    public IReadOnlyList<ChatMessageReaction> Reactions => _reactions.AsReadOnly();

    public static ChatMessage Create(
        Guid id, Guid workspaceId, Guid channelId, Guid? parentMessageId, Guid authorUserId, string body, DateTimeOffset nowUtc,
        IReadOnlyCollection<Guid>? mentionUserIds = null, Func<Guid>? mentionIdFactory = null)
    {
        Guard.AgainstEmpty(authorUserId, nameof(authorUserId));
        var normalized = Validate(body);
        var message = new ChatMessage(id, workspaceId, channelId, parentMessageId, authorUserId, normalized, nowUtc);

        if (mentionUserIds is { Count: > 0 } mentions && mentionIdFactory is not null)
        {
            foreach (var userId in mentions.Distinct())
            {
                message._mentions.Add(new ChatMention(mentionIdFactory(), id, userId));
            }
        }

        return message;
    }

    public bool AddReaction(Guid id, Guid userId, string emoji)
    {
        if (IsDeleted)
        {
            throw new ConflictException("Cannot react to a deleted message.");
        }

        var normalized = emoji.Trim();
        if (_reactions.Any(r => r.UserId == userId && r.Emoji == normalized))
        {
            return false;
        }

        _reactions.Add(new ChatMessageReaction(id, Id, userId, normalized));
        return true;
    }

    public bool RemoveReaction(Guid userId, string emoji)
    {
        var normalized = emoji.Trim();
        var existing = _reactions.FirstOrDefault(r => r.UserId == userId && r.Emoji == normalized);
        if (existing is null)
        {
            return false;
        }

        _reactions.Remove(existing);
        return true;
    }

    public void Edit(string body, Guid editorUserId, DateTimeOffset nowUtc)
    {
        if (IsDeleted)
        {
            throw new ConflictException("A deleted message cannot be edited.");
        }

        if (editorUserId != AuthorUserId)
        {
            throw new ForbiddenException("Only the author can edit this message.");
        }

        Body = Validate(body);
        EditedAtUtc = nowUtc;
    }

    /// <summary>Soft-deletes the message. The author may delete their own; a moderator may delete any.</summary>
    public void Delete(Guid actorUserId, bool isModerator, DateTimeOffset nowUtc)
    {
        if (IsDeleted)
        {
            return;
        }

        if (actorUserId != AuthorUserId && !isModerator)
        {
            throw new ForbiddenException("You can only delete your own messages.");
        }

        IsDeleted = true;
        DeletedAtUtc = nowUtc;
        Body = string.Empty;
    }

    private static string Validate(string body)
    {
        Guard.AgainstNullOrWhiteSpace(body, nameof(body));
        var trimmed = body.Trim();
        if (trimmed.Length > MaxBodyLength)
        {
            throw new ValidationAppException($"A message cannot exceed {MaxBodyLength} characters.");
        }

        return trimmed;
    }
}

/// <summary>A mention of a workspace member inside a chat message (mirrors Collaboration's Comment.Mention).</summary>
public sealed class ChatMention : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private ChatMention()
    {
    }

    public ChatMention(Guid id, Guid messageId, Guid mentionedUserId)
        : base(id)
    {
        MessageId = messageId;
        MentionedUserId = mentionedUserId;
    }

    public Guid MessageId { get; private set; }
    public Guid MentionedUserId { get; private set; }
}

/// <summary>An emoji reaction to a chat message (unique per message+user+emoji; mirrors CommentReaction).</summary>
public sealed class ChatMessageReaction : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private ChatMessageReaction()
    {
    }

    public ChatMessageReaction(Guid id, Guid messageId, Guid userId, string emoji)
        : base(id)
    {
        MessageId = messageId;
        UserId = userId;
        Emoji = emoji;
    }

    public Guid MessageId { get; private set; }
    public Guid UserId { get; private set; }
    public string Emoji { get; private set; } = string.Empty;
}
