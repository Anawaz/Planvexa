namespace Planvexa.Modules.Collaboration.Application;

public sealed record CreateCommentCommand(Guid TaskId, string Body, Guid? ParentId, IReadOnlyList<Guid>? MentionUserIds);

public sealed record ReactionDto(string Emoji, IReadOnlyList<Guid> UserIds);

public sealed record CommentAttachmentDto(
    Guid Id, Guid CommentId, string FileName, string ContentType, long SizeBytes,
    Guid UploadedByUserId, DateTimeOffset CreatedAtUtc);

public sealed record CommentDto(
    Guid Id,
    Guid TaskId,
    Guid? ParentId,
    Guid AuthorUserId,
    string Body,
    bool IsEdited,
    bool IsDeleted,
    IReadOnlyList<Guid> MentionUserIds,
    IReadOnlyList<ReactionDto> Reactions,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    IReadOnlyList<CommentDto> Replies,
    IReadOnlyList<CommentAttachmentDto> Attachments);
