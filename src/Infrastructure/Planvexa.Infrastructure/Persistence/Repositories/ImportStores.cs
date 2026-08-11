namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.WorkManagement.Application;
using Planvexa.Modules.WorkManagement.Domain;

internal sealed class ImportJobStore(PlanvexaDbContext db) : IImportJobStore
{
    public void Add(ImportJob job) => db.Set<ImportJob>().Add(job);

    public Task<ImportJob?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
        => db.Set<ImportJob>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id, ct);

    public async Task<IReadOnlyList<ImportJob>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<ImportJob>().Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
}

internal sealed class ImportJobRowStore(PlanvexaDbContext db) : IImportJobRowStore
{
    public void Add(ImportJobRow row) => db.Set<ImportJobRow>().Add(row);

    public async Task<IReadOnlyList<ImportJobRow>> ListByJobAsync(Guid importJobId, CancellationToken ct = default)
        => await db.Set<ImportJobRow>().Where(x => x.ImportJobId == importJobId)
            .OrderBy(x => x.RowIndex).ToListAsync(ct);

    public async Task<IReadOnlyList<ImportJobRow>> ListPendingOrInvalidAsync(Guid importJobId, CancellationToken ct = default)
        => await db.Set<ImportJobRow>()
            .Where(x => x.ImportJobId == importJobId && (x.Status == ImportRowStatus.Pending || x.Status == ImportRowStatus.Invalid))
            .OrderBy(x => x.RowIndex).ToListAsync(ct);

    public async Task<IReadOnlyList<ImportJobRow>> ListValidNotCommittedAsync(Guid importJobId, CancellationToken ct = default)
        => await db.Set<ImportJobRow>()
            .Where(x => x.ImportJobId == importJobId && x.Status == ImportRowStatus.Valid)
            .OrderBy(x => x.RowIndex).ToListAsync(ct);
}
