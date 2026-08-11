namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Mobile.Application;
using Planvexa.Modules.Mobile.Domain;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Mobile;

internal sealed class DeviceRegistrationStore(PlanvexaDbContext db) : IDeviceRegistrationStore
{
    public void Add(DeviceRegistration device) => db.Set<DeviceRegistration>().Add(device);

    public void Remove(DeviceRegistration device) => db.Set<DeviceRegistration>().Remove(device);

    public Task<DeviceRegistration?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<DeviceRegistration>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<DeviceRegistration?> FindByTokenHashAsync(Guid workspaceId, Guid userId, string tokenHash, CancellationToken ct = default)
        => db.Set<DeviceRegistration>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId && x.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<DeviceRegistration>> ListForUserAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
        => await db.Set<DeviceRegistration>()
            .Where(x => x.WorkspaceId == workspaceId && x.UserId == userId)
            .OrderByDescending(x => x.LastSeenAtUtc).ToListAsync(ct);
}

/// <summary>
/// Implements the cross-module <see cref="IPushDeviceDirectory"/> over mobile.device_registrations, so
/// the Notifications module can check push-delivery eligibility without depending on the Mobile module.
/// </summary>
internal sealed class PushDeviceDirectory(PlanvexaDbContext db) : IPushDeviceDirectory
{
    public Task<bool> HasActiveDeviceAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default)
        => db.Set<DeviceRegistration>().AnyAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId, cancellationToken);
}

/// <summary>
/// Implements the cross-module <see cref="IChangeFeed"/> for mobile delta sync over WorkManagement task
/// rows, so the Mobile module never touches those tables directly. Returns tasks whose UpdatedAtUtc (or
/// CreatedAtUtc for never-updated rows) is at or after the cursor — including soft-deleted tasks, so the
/// client can remove them locally. The next cursor is the max change time observed (or the input cursor).
/// </summary>
internal sealed class ChangeFeed(PlanvexaDbContext db) : IChangeFeed
{
    public async Task<ChangePage> GetChangesAsync(Guid workspaceId, DateTimeOffset sinceUtc, int max, CancellationToken cancellationToken = default)
    {
        var pageSize = max is > 0 and <= 500 ? max : 200;

        // Include soft-deleted tasks so clients can reconcile deletions; bypass the soft-delete filter but
        // re-assert workspace isolation explicitly (IgnoreQueryFilters also drops the workspace filter).
        var rows = await db.Set<WorkItem>().IgnoreQueryFilters()
            .Where(t => t.WorkspaceId == workspaceId)
            .Where(t => (t.UpdatedAtUtc ?? t.CreatedAtUtc) >= sinceUtc)
            .OrderBy(t => t.UpdatedAtUtc ?? t.CreatedAtUtc)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.ListId,
                t.SpaceId,
                t.Title,
                t.Priority,
                t.IsCompleted,
                t.IsDeleted,
                t.DueDate,
                Changed = t.UpdatedAtUtc ?? t.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var changes = rows
            .Select(r => new TaskChange(r.Id, r.ListId, r.SpaceId, r.Title, r.Priority.ToString(), r.IsCompleted, r.IsDeleted, r.DueDate, r.Changed))
            .ToList();

        var nextCursor = changes.Count > 0 ? changes.Max(c => c.ChangedAtUtc) : sinceUtc;
        return new ChangePage(changes, nextCursor);
    }
}
