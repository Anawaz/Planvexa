namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Governance.Application;
using Planvexa.Modules.Governance.Domain;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Governance;

internal sealed class RetentionPolicyStore(PlanvexaDbContext db) : IRetentionPolicyStore
{
    public void Add(RetentionPolicy policy) => db.Set<RetentionPolicy>().Add(policy);

    public Task<RetentionPolicy?> FindAsync(Guid workspaceId, CancellationToken ct = default)
        => db.Set<RetentionPolicy>().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);

    // Cross-workspace read for the background purge worker: bypass the workspace query filter.
    public async Task<IReadOnlyList<RetentionPolicy>> ListAllAsync(CancellationToken ct = default)
        => await db.Set<RetentionPolicy>().IgnoreQueryFilters().ToListAsync(ct);
}

/// <summary>
/// Implements the cross-module <see cref="IRetentionPurger"/> by hard-deleting soft-deleted work items
/// past the retention cutoff, so the Governance module never touches WorkManagement tables directly.
/// Filters explicitly by workspace + <c>IsDeleted</c> + deletion time (bypassing the soft-delete + workspace
/// query filters, which would otherwise hide soft-deleted rows).
/// </summary>
internal sealed class RetentionPurger(PlanvexaDbContext db) : IRetentionPurger
{
    public Task<int> CountPurgeableAsync(Guid workspaceId, DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default)
        => db.Set<WorkItem>().IgnoreQueryFilters()
            .CountAsync(t => t.WorkspaceId == workspaceId && t.IsDeleted && t.DeletedAtUtc != null && t.DeletedAtUtc < cutoffUtc, cancellationToken);

    public async Task<int> PurgeAsync(Guid workspaceId, DateTimeOffset cutoffUtc, int max, CancellationToken cancellationToken = default)
    {
        var expired = await db.Set<WorkItem>().IgnoreQueryFilters()
            .Where(t => t.WorkspaceId == workspaceId && t.IsDeleted && t.DeletedAtUtc != null && t.DeletedAtUtc < cutoffUtc)
            .OrderBy(t => t.DeletedAtUtc)
            .Take(max)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return 0;
        }

        db.Set<WorkItem>().RemoveRange(expired);
        await db.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }
}
