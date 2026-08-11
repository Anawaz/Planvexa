namespace Planvexa.Modules.Collaboration.Application;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Collaboration.Domain;
using Planvexa.SharedContracts.Notifications;
using Planvexa.SharedContracts.Work;
using Planvexa.SharedContracts.Workspaces;

public sealed class CommentService(
    IWorkspaceContextAccessor workspaceAccessor,
    ICurrentUser currentUser,
    ICommentStore comments,
    ITaskDirectory tasks,
    IWorkspaceAccessQuery access,
    INotificationPublisher notifications,
    IRealtimeNotifier realtime,
    IAuditWriter audit,
    IIdGenerator ids,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<CommentDto> AddAsync(CreateCommentCommand command, string? idempotencyKey = null, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(command.TaskId, ct) ?? throw new NotFoundException("Task not found.");

        var callerAccess = await access.GetAccessAsync(task.WorkspaceId, currentUser.UserId, ct);
        if (callerAccess is null || callerAccess.Role < WorkspaceRole.Member)
        {
            throw new ForbiddenException("You do not have permission to comment in this workspace.");
        }

        // Offline-mutation-outbox replay guard: a repeated post with the same Idempotency-Key returns the
        // original comment instead of inserting a duplicate (see Comment.IdempotencyKey's doc comment).
        var key = idempotencyKey?.Trim();
        if (!string.IsNullOrEmpty(key))
        {
            var existing = await comments.FindByIdempotencyKeyAsync(task.WorkspaceId, key, ct);
            if (existing is not null)
            {
                return await BuildThreadDtoAsync(existing.Id, ct);
            }
        }

        // Threaded one level: a reply's parent must be a top-level comment on the same task.
        if (command.ParentId is { } parentId)
        {
            var parent = await comments.FindAsync(parentId, ct) ?? throw new NotFoundException("Parent comment not found.");
            if (parent.TaskId != task.TaskId)
            {
                throw new ValidationAppException("A reply must belong to the same task as its parent.");
            }

            if (parent.ParentId is not null)
            {
                throw new ValidationAppException("Replies can only be added to top-level comments.");
            }
        }

        // Validate mentions are members of the workspace (prevents cross-workspace notification leakage).
        var validMentions = await ValidateMentionsAsync(task.WorkspaceId, command.MentionUserIds, ct);

        var now = clock.UtcNow;
        var comment = Comment.Create(
            ids.NewId(), task.WorkspaceId, task.TaskId, command.ParentId, currentUser.UserId,
            command.Body, validMentions, ids.NewId, now, key);

        comments.Add(comment);
        audit.Write("comment.posted", nameof(Comment), comment.Id, new { task.TaskId });

        // Notify mentioned users (durable inbox + email per their preferences). Deduped per comment+user.
        foreach (var userId in validMentions.Where(u => u != currentUser.UserId))
        {
            await notifications.PublishAsync(new NotificationRequest(
                RecipientUserId: userId,
                EventType: "mention",
                EntityType: "Task",
                EntityId: task.TaskId,
                WorkspaceId: task.WorkspaceId,
                DeduplicationKey: $"mention:{comment.Id:N}:{userId:N}",
                Payload: new Dictionary<string, string>
                {
                    ["taskTitle"] = task.Title,
                    ["commentId"] = comment.Id.ToString(),
                    ["byUserId"] = currentUser.UserId.ToString(),
                }), ct);
        }

        await unitOfWork.SaveChangesAsync(ct);

        await realtime.NotifyAsync(new RealtimeEvent(
            task.WorkspaceId, "Comment", comment.Id, "created", null, workspaceAccessor.Current.CorrelationId), ct);

        return await BuildThreadDtoAsync(comment.Id, ct);
    }

    public async Task<IReadOnlyList<CommentDto>> ListForTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Task not found.");
        var callerAccess = await access.GetAccessAsync(task.WorkspaceId, currentUser.UserId, ct);
        if (callerAccess is null)
        {
            throw new ForbiddenException("You do not have access to this workspace.");
        }

        var all = await comments.ListForTaskAsync(taskId, ct);
        return BuildThreads(all);
    }

    public async Task<CommentDto> EditAsync(Guid commentId, string body, CancellationToken ct = default)
    {
        var comment = await comments.FindWithChildrenAsync(commentId, ct) ?? throw new NotFoundException("Comment not found.");
        var callerAccess = await access.GetAccessAsync(comment.WorkspaceId, currentUser.UserId, ct);
        if (callerAccess is null || callerAccess.Role < WorkspaceRole.Member)
        {
            throw new ForbiddenException("You do not have permission to edit comments here.");
        }

        comment.Edit(body, currentUser.UserId, clock.UtcNow);
        audit.Write("comment.edited", nameof(Comment), comment.Id);
        await unitOfWork.SaveChangesAsync(ct);
        await realtime.NotifyAsync(new RealtimeEvent(comment.WorkspaceId, "Comment", comment.Id, "updated", null, workspaceAccessor.Current.CorrelationId), ct);
        return ToDto(comment, Array.Empty<Comment>());
    }

    public async Task DeleteAsync(Guid commentId, CancellationToken ct = default)
    {
        var comment = await comments.FindAsync(commentId, ct) ?? throw new NotFoundException("Comment not found.");
        var callerAccess = await access.GetAccessAsync(comment.WorkspaceId, currentUser.UserId, ct);
        var isAuthor = comment.AuthorUserId == currentUser.UserId;
        if (callerAccess is null || (!isAuthor && callerAccess.Role < WorkspaceRole.Admin))
        {
            throw new ForbiddenException("Only the author or an admin can delete this comment.");
        }

        comment.SoftDelete(currentUser.UserId, clock.UtcNow);
        audit.Write("comment.deleted", nameof(Comment), comment.Id);
        await unitOfWork.SaveChangesAsync(ct);
        await realtime.NotifyAsync(new RealtimeEvent(comment.WorkspaceId, "Comment", comment.Id, "deleted", null, workspaceAccessor.Current.CorrelationId), ct);
    }

    public async Task<CommentDto> AddReactionAsync(Guid commentId, string emoji, CancellationToken ct = default)
    {
        var comment = await LoadForReactAsync(commentId, ct);
        if (comment.AddReaction(ids.NewId(), currentUser.UserId, emoji))
        {
            await unitOfWork.SaveChangesAsync(ct);
            await realtime.NotifyAsync(new RealtimeEvent(comment.WorkspaceId, "Comment", comment.Id, "reacted", null, workspaceAccessor.Current.CorrelationId), ct);
        }

        return ToDto(comment, Array.Empty<Comment>());
    }

    public async Task<CommentDto> RemoveReactionAsync(Guid commentId, string emoji, CancellationToken ct = default)
    {
        var comment = await LoadForReactAsync(commentId, ct);
        if (comment.RemoveReaction(currentUser.UserId, emoji))
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        return ToDto(comment, Array.Empty<Comment>());
    }

    private async Task<Comment> LoadForReactAsync(Guid commentId, CancellationToken ct)
    {
        var comment = await comments.FindWithChildrenAsync(commentId, ct) ?? throw new NotFoundException("Comment not found.");
        var callerAccess = await access.GetAccessAsync(comment.WorkspaceId, currentUser.UserId, ct);
        if (callerAccess is null || callerAccess.Role < WorkspaceRole.Member)
        {
            throw new ForbiddenException("You do not have permission to react here.");
        }

        return comment;
    }

    private async Task<IReadOnlyList<Guid>> ValidateMentionsAsync(Guid workspaceId, IReadOnlyList<Guid>? mentionUserIds, CancellationToken ct)
    {
        if (mentionUserIds is null || mentionUserIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var valid = new List<Guid>();
        foreach (var userId in mentionUserIds.Distinct())
        {
            var memberAccess = await access.GetAccessAsync(workspaceId, userId, ct);
            if (memberAccess is not null)
            {
                valid.Add(userId);
            }
        }

        return valid;
    }

    private async Task<CommentDto> BuildThreadDtoAsync(Guid commentId, CancellationToken ct)
    {
        var comment = await comments.FindWithChildrenAsync(commentId, ct)!;
        return ToDto(comment!, Array.Empty<Comment>());
    }

    private static IReadOnlyList<CommentDto> BuildThreads(IReadOnlyList<Comment> all)
    {
        var byParent = all.Where(c => c.ParentId is not null).ToLookup(c => c.ParentId!.Value);
        return all
            .Where(c => c.ParentId is null)
            .OrderBy(c => c.CreatedAtUtc)
            .Select(c => ToDto(c, byParent[c.Id].OrderBy(r => r.CreatedAtUtc).ToList()))
            .ToList();
    }

    private static CommentDto ToDto(Comment c, IReadOnlyList<Comment> replies) => new(
        c.Id, c.TaskId, c.ParentId, c.AuthorUserId, c.Body, c.IsEdited, c.IsDeleted,
        c.Mentions.Select(m => m.MentionedUserId).ToList(),
        c.Reactions.GroupBy(r => r.Emoji).Select(g => new ReactionDto(g.Key, g.Select(r => r.UserId).ToList())).ToList(),
        c.CreatedAtUtc, c.UpdatedAtUtc,
        replies.Select(r => ToDto(r, Array.Empty<Comment>())).ToList());
}
