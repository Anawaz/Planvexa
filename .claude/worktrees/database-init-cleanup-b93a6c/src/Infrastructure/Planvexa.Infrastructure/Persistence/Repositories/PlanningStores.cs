namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Planning.Application;
using Planvexa.Modules.Planning.Domain;

internal sealed class WorkScheduleStore(PlanvexaDbContext db) : IWorkScheduleStore
{
    public void Add(WorkSchedule schedule) => db.Set<WorkSchedule>().Add(schedule);

    public Task<WorkSchedule?> FindAsync(Guid workspaceId, CancellationToken ct = default)
        => db.Set<WorkSchedule>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);
}

internal sealed class HolidayStore(PlanvexaDbContext db) : IHolidayStore
{
    public void Add(Holiday holiday) => db.Set<Holiday>().Add(holiday);

    public void Remove(Holiday holiday) => db.Set<Holiday>().Remove(holiday);

    public Task<Holiday?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<Holiday>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Holiday>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<Holiday>().Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.DateUtc).ToListAsync(ct);

    public async Task<IReadOnlyList<Holiday>> ListInRangeAsync(Guid workspaceId, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
        => await db.Set<Holiday>()
            .Where(x => x.WorkspaceId == workspaceId && x.DateUtc >= fromDate.Date && x.DateUtc <= toDate.Date)
            .ToListAsync(ct);
}

internal sealed class LeaveStore(PlanvexaDbContext db) : ILeaveStore
{
    public void Add(LeaveEntry entry) => db.Set<LeaveEntry>().Add(entry);

    public void Remove(LeaveEntry entry) => db.Set<LeaveEntry>().Remove(entry);

    public Task<LeaveEntry?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<LeaveEntry>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<LeaveEntry>> ListByWorkspaceAsync(Guid workspaceId, Guid? userId, CancellationToken ct = default)
    {
        var query = db.Set<LeaveEntry>().Where(x => x.WorkspaceId == workspaceId);
        if (userId is { } uid)
        {
            query = query.Where(x => x.UserId == uid);
        }

        return await query.OrderBy(x => x.StartDate).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LeaveEntry>> ListForUserInRangeAsync(Guid workspaceId, Guid userId, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
        => await db.Set<LeaveEntry>()
            .Where(x => x.WorkspaceId == workspaceId && x.UserId == userId
                && x.StartDate <= toDate.Date && x.EndDate >= fromDate.Date)
            .ToListAsync(ct);
}

internal sealed class EstimateStore(PlanvexaDbContext db) : IEstimateStore
{
    public void Add(TaskEstimate estimate) => db.Set<TaskEstimate>().Add(estimate);

    public Task<TaskEstimate?> FindAsync(Guid workspaceId, Guid taskId, CancellationToken ct = default)
        => db.Set<TaskEstimate>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.TaskId == taskId, ct);

    public async Task<IReadOnlyList<TaskEstimate>> ListByTaskIdsAsync(Guid workspaceId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default)
    {
        if (taskIds.Count == 0)
        {
            return Array.Empty<TaskEstimate>();
        }

        return await db.Set<TaskEstimate>()
            .Where(x => x.WorkspaceId == workspaceId && taskIds.Contains(x.TaskId))
            .ToListAsync(ct);
    }
}

internal sealed class SprintStore(PlanvexaDbContext db) : ISprintStore
{
    public void Add(Sprint sprint) => db.Set<Sprint>().Add(sprint);

    public void Remove(Sprint sprint) => db.Set<Sprint>().Remove(sprint);

    public Task<Sprint?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<Sprint>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Sprint?> FindWithItemsAsync(Guid id, CancellationToken ct = default)
        => db.Set<Sprint>().Include(s => s.Items).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Sprint>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<Sprint>().Include(s => s.Items)
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.StartDate).ToListAsync(ct);
}
