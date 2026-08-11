namespace Planvexa.Modules.Collaboration.Application;

using Planvexa.Modules.Collaboration.Domain;

public interface ICommentAttachmentStore
{
    void Add(CommentAttachment attachment);
    void Remove(CommentAttachment attachment);
    Task<CommentAttachment?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Batch load for a whole thread — <see cref="CommentService"/> builds every comment's DTO
    /// (root + replies) in one round trip rather than one query per comment.</summary>
    Task<IReadOnlyList<CommentAttachment>> ListForCommentsAsync(IReadOnlyList<Guid> commentIds, CancellationToken ct = default);
}
