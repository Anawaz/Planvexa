namespace Planvexa.Modules.Collaboration.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.SharedContracts.IntegrationEvents;

/// <summary>
/// A comment on a task. Threaded one level via <see cref="ParentId"/> (replies). Mentions are stored
/// as child rows referencing validated workspace members. Soft-deleted to preserve thread structure.
/// </summary>
public sealed class Comment : Entity, IAggregateRoot, IWorkspaceOwned, ISoftDeletable
{
    private readonly List<Mention> _mentions = new();
    private readonly List<CommentReaction> _reactions = new();

    private Comment()
    {
    }

    private Comment(Guid id, Guid workspaceId, Guid taskId, Guid? parentId, Guid authorUserId, string body, string? idempotencyKey, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        TaskId = taskId;
        ParentId = parentId;
        AuthorUserId = authorUserId;
        Body = body;
        IdempotencyKey = idempotencyKey;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid? ParentId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public bool IsEdited { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    /// <summary>Offline-mutation-outbox replay guard: see WorkItem.IdempotencyKey's doc comment for the
    /// pattern (nullable, unique per workspace when set, checked before creating).</summary>
    public string? IdempotencyKey { get; private set; }

    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAtUtc { get; private set; }
    public Guid? DeletedByUserId { get; private set; }

    public IReadOnlyList<Mention> Mentions => _mentions.AsReadOnly();
    public IReadOnlyList<CommentReaction> Reactions => _reactions.AsReadOnly();

    public static Comment Create(
        Guid id, Guid workspaceId, Guid taskId, Guid? parentId, Guid authorUserId,
        string body, IReadOnlyCollection<Guid> mentionUserIds, Func<Guid> idFactory, DateTimeOffset nowUtc,
        string? idempotencyKey = null)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(taskId, nameof(taskId));
        Guard.AgainstEmpty(authorUserId, nameof(authorUserId));
        Guard.AgainstNullOrWhiteSpace(body, nameof(body));

        var comment = new Comment(id, workspaceId, taskId, parentId, authorUserId, body.Trim(), idempotencyKey, nowUtc);
        foreach (var userId in mentionUserIds.Distinct())
        {
            comment._mentions.Add(new Mention(idFactory(), id, taskId, userId));
        }

        comment.Raise(new CommentPostedIntegrationEvent(workspaceId, taskId, id, authorUserId));
        foreach (var mention in comment._mentions.Where(m => m.MentionedUserId != authorUserId))
        {
            comment.Raise(new UserMentionedIntegrationEvent(workspaceId, taskId, id, mention.MentionedUserId, authorUserId));
        }

        return comment;
    }

    public void Edit(string body, Guid editorUserId, DateTimeOffset nowUtc)
    {
        if (editorUserId != AuthorUserId)
        {
            throw new BuildingBlocks.Exceptions.ForbiddenException("Only the author can edit this comment.");
        }

        Body = Guard.AgainstNullOrWhiteSpace(body, nameof(body)).Trim();
        IsEdited = true;
        UpdatedAtUtc = nowUtc;
    }

    public void SoftDelete(Guid userId, DateTimeOffset nowUtc)
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        DeletedAtUtc = nowUtc;
        DeletedByUserId = userId;
        Body = string.Empty;
    }

    public bool AddReaction(Guid id, Guid userId, string emoji)
    {
        var normalized = emoji.Trim();
        if (_reactions.Any(r => r.UserId == userId && r.Emoji == normalized))
        {
            return false;
        }

        _reactions.Add(new CommentReaction(id, Id, userId, normalized));
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
}

/// <summary>A mention of a workspace member inside a comment.</summary>
public sealed class Mention : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private Mention()
    {
    }

    public Mention(Guid id, Guid commentId, Guid taskId, Guid mentionedUserId)
        : base(id)
    {
        CommentId = commentId;
        TaskId = taskId;
        MentionedUserId = mentionedUserId;
    }

    public Guid CommentId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid MentionedUserId { get; private set; }
}

/// <summary>An emoji reaction to a comment (unique per comment+user+emoji).</summary>
public sealed class CommentReaction : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private CommentReaction()
    {
    }

    public CommentReaction(Guid id, Guid commentId, Guid userId, string emoji)
        : base(id)
    {
        CommentId = commentId;
        UserId = userId;
        Emoji = emoji;
    }

    public Guid CommentId { get; private set; }
    public Guid UserId { get; private set; }
    public string Emoji { get; private set; } = string.Empty;
}
