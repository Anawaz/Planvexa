namespace Planvexa.Modules.Collaboration.Application;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.Modules.Collaboration.Domain;
using Planvexa.SharedContracts.Collaboration;
using Planvexa.SharedContracts.Work;

/// <summary>
/// Implements the cross-module <see cref="ICommentWriteApi"/> so Automations can post a comment on a
/// task (the "comment" action) without depending on Collaboration internals. Skips the
/// interactive-only concerns of <see cref="CommentService.AddAsync"/> (ambient <c>ICurrentUser</c>,
/// mention validation/notification, realtime broadcast) since the caller supplies an explicit author and
/// there is no interactive mentioner — the comment itself still flows through the normal
/// <see cref="Comment"/> aggregate so it is visible via the normal comment list/search paths.
/// </summary>
public sealed class CommentWriteApi(
    ITaskDirectory tasks,
    ICommentStore comments,
    IIdGenerator ids,
    IClock clock,
    IUnitOfWork unitOfWork) : ICommentWriteApi
{
    public async Task<Guid?> PostSystemCommentAsync(Guid workspaceId, Guid taskId, Guid authorUserId, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var task = await tasks.FindAsync(taskId, cancellationToken);
        if (task is null || task.WorkspaceId != workspaceId)
        {
            return null;
        }

        var comment = Comment.Create(
            ids.NewId(), workspaceId, taskId, parentId: null, authorUserId,
            body, Array.Empty<Guid>(), ids.NewId, clock.UtcNow);

        comments.Add(comment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return comment.Id;
    }
}
