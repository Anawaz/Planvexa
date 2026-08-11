namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Governance.Application;
using Planvexa.Modules.Governance.Domain;

internal sealed class SecuritySettingsStore(PlanvexaDbContext db) : ISecuritySettingsStore
{
    public void Add(EnterpriseSecuritySettings settings) => db.Set<EnterpriseSecuritySettings>().Add(settings);

    public Task<EnterpriseSecuritySettings?> FindAsync(Guid workspaceId, CancellationToken ct = default)
        => db.Set<EnterpriseSecuritySettings>().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);
}

internal sealed class WorkspaceIpAllowRuleStore(PlanvexaDbContext db) : IWorkspaceIpAllowRuleStore
{
    public void Add(WorkspaceIpAllowRule rule) => db.Set<WorkspaceIpAllowRule>().Add(rule);

    public void Remove(WorkspaceIpAllowRule rule) => db.Set<WorkspaceIpAllowRule>().Remove(rule);

    public Task<WorkspaceIpAllowRule?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<WorkspaceIpAllowRule>().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);

    // IgnoreQueryFilters + explicit workspaceId filter, same reasoning as SecuritySettingsStore.FindAsync
    // above: IpAllowListMiddleware calls this before authorization/role checks run, so it must not depend
    // on the ambient ASP.NET Core request pipeline having already bound the EF workspace query filter.
    public async Task<IReadOnlyList<WorkspaceIpAllowRule>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<WorkspaceIpAllowRule>().IgnoreQueryFilters()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);
}

internal sealed class ExportJobStore(PlanvexaDbContext db) : IExportJobStore
{
    public void Add(ExportJob job) => db.Set<ExportJob>().Add(job);

    public Task<ExportJob?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<ExportJob>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<ExportJob>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<ExportJob>()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);

    // Cross-workspace read for the background worker: bypass the workspace query filter so pending jobs
    // from every workspace are visible; the worker binds each job's workspace before processing it.
    public async Task<IReadOnlyList<ExportJob>> ListPendingAsync(int max, CancellationToken ct = default)
        => await db.Set<ExportJob>().IgnoreQueryFilters()
            .Where(x => x.Status == ExportJobStatus.Pending)
            .OrderBy(x => x.CreatedAtUtc).Take(max).ToListAsync(ct);
}
