namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Collaboration.Application;
using Planvexa.Modules.Collaboration.Domain;

internal sealed class CommentStore(PlanvexaDbContext db) : ICommentStore
{
    public void Add(Comment comment) => db.Set<Comment>().Add(comment);

    public Task<Comment?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<Comment>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Comment?> FindWithChildrenAsync(Guid id, CancellationToken ct = default)
        => db.Set<Comment>().Include(c => c.Mentions).Include(c => c.Reactions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Comment>> ListForTaskAsync(Guid taskId, CancellationToken ct = default)
        => await db.Set<Comment>().Include(c => c.Mentions).Include(c => c.Reactions)
            .Where(x => x.TaskId == taskId)
            .OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);

    public async Task<IReadOnlyList<Comment>> SearchByWorkspaceAsync(Guid workspaceId, string contains, int take, CancellationToken ct = default)
        => await db.Set<Comment>()
            .Where(x => x.WorkspaceId == workspaceId && !x.IsDeleted && EF.Functions.ILike(x.Body, contains))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);

    public Task<Comment?> FindByIdempotencyKeyAsync(Guid workspaceId, string key, CancellationToken ct = default)
        => db.Set<Comment>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.IdempotencyKey == key, ct);
}

internal sealed class CommentAttachmentStore(PlanvexaDbContext db) : ICommentAttachmentStore
{
    public void Add(CommentAttachment attachment) => db.Set<CommentAttachment>().Add(attachment);

    public void Remove(CommentAttachment attachment) => db.Set<CommentAttachment>().Remove(attachment);

    public Task<CommentAttachment?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<CommentAttachment>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<CommentAttachment>> ListForCommentsAsync(IReadOnlyList<Guid> commentIds, CancellationToken ct = default)
        => await db.Set<CommentAttachment>()
            .Where(x => commentIds.Contains(x.CommentId))
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);
}

internal sealed class ShareLinkStore(PlanvexaDbContext db, MaintenanceConnection maintenance) : IShareLinkStore
{
    public void Add(PublicShareLink link) => db.Set<PublicShareLink>().Add(link);

    public Task<PublicShareLink?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<PublicShareLink>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<PublicShareLink?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default)
        => maintenance.LookupAsync(db, () =>
            db.Set<PublicShareLink>().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct));

    public async Task<IReadOnlyList<PublicShareLink>> ListForTaskAsync(Guid taskId, CancellationToken ct = default)
        => await db.Set<PublicShareLink>().Where(x => x.TaskId == taskId)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
}

internal sealed class PublicCommentStore(PlanvexaDbContext db) : IPublicCommentStore
{
    public void Add(PublicComment comment) => db.Set<PublicComment>().Add(comment);

    public async Task<IReadOnlyList<PublicComment>> ListForShareLinkAsync(Guid shareLinkId, CancellationToken ct = default)
        => await db.Set<PublicComment>().Where(x => x.ShareLinkId == shareLinkId)
            .OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);
}
