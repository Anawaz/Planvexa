namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Goals.Application;
using Planvexa.Modules.Goals.Domain;

internal sealed class GoalStore(PlanvexaDbContext db) : IGoalStore
{
    public void Add(Goal goal) => db.Set<Goal>().Add(goal);

    public void Remove(Goal goal) => db.Set<Goal>().Remove(goal);

    public Task<Goal?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
        => db.Set<Goal>().Include(g => g.LinkedTasks).Include(g => g.KeyResults)
            .FirstOrDefaultAsync(g => g.WorkspaceId == workspaceId && g.Id == id, ct);

    public Task<Goal?> FindWithLinkedTasksAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
        => FindAsync(workspaceId, id, ct);

    public async Task<IReadOnlyList<Goal>> ListByWorkspaceAsync(Guid workspaceId, Guid? folderId, CancellationToken ct = default)
    {
        var query = db.Set<Goal>().Include(g => g.LinkedTasks).Include(g => g.KeyResults).Where(g => g.WorkspaceId == workspaceId);
        if (folderId is { } fid)
        {
            query = query.Where(g => g.FolderId == fid);
        }

        return await query.OrderBy(g => g.Name).ToListAsync(ct);
    }
}

internal sealed class GoalFolderStore(PlanvexaDbContext db) : IGoalFolderStore
{
    public void Add(GoalFolder folder) => db.Set<GoalFolder>().Add(folder);

    public void Remove(GoalFolder folder) => db.Set<GoalFolder>().Remove(folder);

    public Task<GoalFolder?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
        => db.Set<GoalFolder>().FirstOrDefaultAsync(f => f.WorkspaceId == workspaceId && f.Id == id, ct);

    public async Task<IReadOnlyList<GoalFolder>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<GoalFolder>().Where(f => f.WorkspaceId == workspaceId).OrderBy(f => f.Name).ToListAsync(ct);
}

internal sealed class GoalCommentStore(PlanvexaDbContext db) : IGoalCommentStore
{
    public void Add(GoalComment comment) => db.Set<GoalComment>().Add(comment);

    public async Task<IReadOnlyList<GoalComment>> ListByGoalAsync(Guid workspaceId, Guid goalId, CancellationToken ct = default)
        => await db.Set<GoalComment>()
            .Where(c => c.WorkspaceId == workspaceId && c.GoalId == goalId)
            .OrderBy(c => c.CreatedAtUtc)
            .ToListAsync(ct);
}
