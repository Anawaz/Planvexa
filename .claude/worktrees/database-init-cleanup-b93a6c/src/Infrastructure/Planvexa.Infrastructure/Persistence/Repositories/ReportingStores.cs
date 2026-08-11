namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Reporting.Application;
using Planvexa.Modules.Reporting.Domain;

internal sealed class DashboardStore(PlanvexaDbContext db) : IDashboardStore
{
    public void Add(Dashboard dashboard) => db.Set<Dashboard>().Add(dashboard);

    public void Remove(Dashboard dashboard) => db.Set<Dashboard>().Remove(dashboard);

    public Task<Dashboard?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<Dashboard>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Dashboard?> FindWithWidgetsAsync(Guid id, CancellationToken ct = default)
        => db.Set<Dashboard>().Include(d => d.Widgets).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Dashboard>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<Dashboard>().Include(d => d.Widgets)
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.Name).ToListAsync(ct);
}
