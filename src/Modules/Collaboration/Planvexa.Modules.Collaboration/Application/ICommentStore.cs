namespace Planvexa.Modules.Collaboration.Application;

using Planvexa.Modules.Collaboration.Domain;

public interface ICommentStore
{
    void Add(Comment comment);
    Task<Comment?> FindAsync(Guid id, CancellationToken ct = default);
    Task<Comment?> FindWithChildrenAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Comment>> ListForTaskAsync(Guid taskId, CancellationToken ct = default);

    /// <summary>search: body matches across every (non-deleted) comment in the workspace, newest
    /// first. Not scoped to a task — the caller (CommentSearchProvider) filters per-task read access
    /// itself, this store does not know about task privacy/ACL.</summary>
    Task<IReadOnlyList<Comment>> SearchByWorkspaceAsync(Guid workspaceId, string contains, int take, CancellationToken ct = default);

    /// <summary>Offline-mutation-outbox replay guard: the comment previously created with this
    /// Idempotency-Key in this workspace, if any (see Comment.IdempotencyKey's doc comment).</summary>
    Task<Comment?> FindByIdempotencyKeyAsync(Guid workspaceId, string key, CancellationToken ct = default);
}
