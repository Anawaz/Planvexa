namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Reporting.Application;
using Planvexa.Modules.Reporting.Domain;

internal sealed class RiskStore(PlanvexaDbContext db) : IRiskStore
{
    public void Add(Risk risk) => db.Set<Risk>().Add(risk);

    public void Remove(Risk risk) => db.Set<Risk>().Remove(risk);

    public Task<Risk?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
        => db.Set<Risk>().FirstOrDefaultAsync(r => r.WorkspaceId == workspaceId && r.Id == id, ct);

    public async Task<IReadOnlyList<Risk>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<Risk>().Where(r => r.WorkspaceId == workspaceId).OrderByDescending(r => r.Severity).ToListAsync(ct);
}

internal sealed class ScheduledReportStore(PlanvexaDbContext db) : IScheduledReportStore
{
    public void Add(ScheduledReport report) => db.Set<ScheduledReport>().Add(report);

    public void Remove(ScheduledReport report) => db.Set<ScheduledReport>().Remove(report);

    public Task<ScheduledReport?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
        => db.Set<ScheduledReport>().FirstOrDefaultAsync(r => r.WorkspaceId == workspaceId && r.Id == id, ct);

    public async Task<IReadOnlyList<ScheduledReport>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<ScheduledReport>().Where(r => r.WorkspaceId == workspaceId).ToListAsync(ct);

    public async Task<IReadOnlyList<ScheduledReport>> ListEnabledAsync(CancellationToken ct = default)
        => await db.Set<ScheduledReport>().IgnoreQueryFilters().Where(r => r.IsEnabled).ToListAsync(ct);
}
