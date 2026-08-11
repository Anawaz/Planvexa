namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.WorkManagement.Application;
using Planvexa.Modules.WorkManagement.Domain;

internal sealed class SpaceStore(PlanvexaDbContext db) : ISpaceStore
{
    public void Add(Space space) => db.Set<Space>().Add(space);

    public Task<Space?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<Space>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Space>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<Space>().Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.Position).ToListAsync(ct);

    public async Task<double?> MaxPositionAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<Space>().Where(x => x.WorkspaceId == workspaceId)
            .Select(x => (double?)x.Position).MaxAsync(ct);
}

internal sealed class FolderStore(PlanvexaDbContext db) : IFolderStore
{
    public void Add(Folder folder) => db.Set<Folder>().Add(folder);

    public Task<Folder?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<Folder>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Folder>> ListBySpaceAsync(Guid spaceId, CancellationToken ct = default)
        => await db.Set<Folder>().Where(x => x.SpaceId == spaceId)
            .OrderBy(x => x.Position).ToListAsync(ct);

    public async Task<double?> MaxPositionAsync(Guid spaceId, CancellationToken ct = default)
        => await db.Set<Folder>().Where(x => x.SpaceId == spaceId)
            .Select(x => (double?)x.Position).MaxAsync(ct);
}

internal sealed class TaskListStore(PlanvexaDbContext db) : ITaskListStore
{
    public void Add(TaskList list) => db.Set<TaskList>().Add(list);

    public Task<TaskList?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<TaskList>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<TaskList>> ListBySpaceAsync(Guid spaceId, CancellationToken ct = default)
        => await db.Set<TaskList>().Where(x => x.SpaceId == spaceId)
            .OrderBy(x => x.Position).ToListAsync(ct);

    public async Task<double?> MaxPositionAsync(Guid spaceId, CancellationToken ct = default)
        => await db.Set<TaskList>().Where(x => x.SpaceId == spaceId)
            .Select(x => (double?)x.Position).MaxAsync(ct);
}

internal sealed class StatusSchemeStore(PlanvexaDbContext db) : IStatusSchemeStore
{
    public void Add(StatusScheme scheme) => db.Set<StatusScheme>().Add(scheme);

    public Task<StatusScheme?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<StatusScheme>().Include(s => s.Statuses).FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<StatusScheme?> FindDefaultAsync(Guid workspaceId, CancellationToken ct = default)
        => db.Set<StatusScheme>().Include(s => s.Statuses)
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.IsDefault, ct);

    public async Task<IReadOnlyList<StatusScheme>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<StatusScheme>().Include(s => s.Statuses)
            .Where(x => x.WorkspaceId == workspaceId).ToListAsync(ct);

    public Task<StatusDefinition?> FindStatusAsync(Guid statusId, CancellationToken ct = default)
        => db.Set<StatusDefinition>().FirstOrDefaultAsync(x => x.Id == statusId, ct);
}

internal sealed class WorkTemplateStore(PlanvexaDbContext db) : IWorkTemplateStore
{
    public void Add(WorkTemplate template) => db.Set<WorkTemplate>().Add(template);

    public Task<WorkTemplate?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<WorkTemplate>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<WorkTemplate>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<WorkTemplate>().Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
}

internal sealed class WorkFavoriteStore(PlanvexaDbContext db) : IWorkFavoriteStore
{
    public void Add(WorkFavorite favorite) => db.Set<WorkFavorite>().Add(favorite);

    public void Remove(WorkFavorite favorite) => db.Set<WorkFavorite>().Remove(favorite);

    public Task<WorkFavorite?> FindAsync(Guid workspaceId, Guid userId, string resourceType, Guid resourceId, CancellationToken ct = default)
        => db.Set<WorkFavorite>().FirstOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.UserId == userId && x.ResourceType == resourceType && x.ResourceId == resourceId, ct);

    public async Task<IReadOnlyList<WorkFavorite>> ListForUserAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
        => await db.Set<WorkFavorite>().Where(x => x.WorkspaceId == workspaceId && x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
}

internal sealed class RecentItemStore(PlanvexaDbContext db) : IRecentItemStore
{
    public void Add(RecentItem item) => db.Set<RecentItem>().Add(item);

    public void Remove(RecentItem item) => db.Set<RecentItem>().Remove(item);

    public Task<RecentItem?> FindAsync(Guid workspaceId, Guid userId, string resourceType, Guid resourceId, CancellationToken ct = default)
        => db.Set<RecentItem>().FirstOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.UserId == userId && x.ResourceType == resourceType && x.ResourceId == resourceId, ct);

    public async Task<IReadOnlyList<RecentItem>> ListForUserAsync(Guid workspaceId, Guid userId, int take, CancellationToken ct = default)
        => await db.Set<RecentItem>().Where(x => x.WorkspaceId == workspaceId && x.UserId == userId)
            .OrderByDescending(x => x.ViewedAtUtc).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<RecentItem>> ListOverflowAsync(Guid workspaceId, Guid userId, int keep, CancellationToken ct = default)
        => await db.Set<RecentItem>().Where(x => x.WorkspaceId == workspaceId && x.UserId == userId)
            .OrderByDescending(x => x.ViewedAtUtc).Skip(keep).ToListAsync(ct);
}

internal sealed class TagStore(PlanvexaDbContext db) : ITagStore
{
    public void Add(Tag tag) => db.Set<Tag>().Add(tag);

    public async Task<IReadOnlyList<Tag>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<Tag>().Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> ExistingTagIdsAsync(Guid workspaceId, IReadOnlyCollection<Guid> tagIds, CancellationToken ct = default)
        => await db.Set<Tag>().Where(x => x.WorkspaceId == workspaceId && tagIds.Contains(x.Id))
            .Select(x => x.Id).ToListAsync(ct);
}
