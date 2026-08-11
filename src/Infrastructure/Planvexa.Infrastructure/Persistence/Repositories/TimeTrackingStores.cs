namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.TimeTracking.Application;
using Planvexa.Modules.TimeTracking.Domain;

internal sealed class TimeEntryStore(PlanvexaDbContext db) : ITimeEntryStore
{
    public void Add(TimeEntry entry) => db.Set<TimeEntry>().Add(entry);

    public void Remove(TimeEntry entry) => db.Set<TimeEntry>().Remove(entry);

    public void AddAudit(TimeEntryAudit audit) => db.Set<TimeEntryAudit>().Add(audit);

    public Task<TimeEntry?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<TimeEntry>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<TimeEntry?> FindActiveForUserAsync(Guid userId, CancellationToken ct = default)
        => db.Set<TimeEntry>().FirstOrDefaultAsync(x => x.UserId == userId && x.EndedAtUtc == null, ct);

    public async Task<IReadOnlyList<TimeEntry>> QueryAsync(
        Guid workspaceId, Guid? userId, Guid? taskId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, Guid? tagId = null, CancellationToken ct = default)
    {
        var query = db.Set<TimeEntry>().Include(x => x.Tags).Where(x => x.WorkspaceId == workspaceId);
        if (userId is { } uid)
        {
            query = query.Where(x => x.UserId == uid);
        }

        if (taskId is { } tid)
        {
            query = query.Where(x => x.TaskId == tid);
        }

        if (fromUtc is { } from)
        {
            query = query.Where(x => x.StartedAtUtc >= from);
        }

        if (toUtc is { } to)
        {
            query = query.Where(x => x.StartedAtUtc < to);
        }

        if (tagId is { } tag)
        {
            query = query.Where(x => x.Tags.Any(t => t.TagId == tag));
        }

        return await query.OrderByDescending(x => x.StartedAtUtc).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TimeEntry>> ListForPeriodAsync(
        Guid workspaceId, Guid userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
        => await db.Set<TimeEntry>().Include(x => x.Tags)
            .Where(x => x.WorkspaceId == workspaceId && x.UserId == userId
                && x.StartedAtUtc >= fromUtc && x.StartedAtUtc < toUtc)
            .OrderBy(x => x.StartedAtUtc).ToListAsync(ct);

    public Task<long> SumDurationSecondsAsync(Guid workspaceId, Guid userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
        => db.Set<TimeEntry>()
            .Where(x => x.WorkspaceId == workspaceId && x.UserId == userId
                && x.StartedAtUtc >= fromUtc && x.StartedAtUtc < toUtc && x.EndedAtUtc != null)
            .SumAsync(x => x.DurationSeconds, ct);

    public Task<TimeEntry?> FindByIdempotencyKeyAsync(Guid workspaceId, string key, CancellationToken ct = default)
        => db.Set<TimeEntry>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.IdempotencyKey == key, ct);
}

internal sealed class TimePolicyStore(PlanvexaDbContext db) : ITimePolicyStore
{
    public void Add(TimePolicy policy) => db.Set<TimePolicy>().Add(policy);

    public Task<TimePolicy?> FindAsync(Guid workspaceId, CancellationToken ct = default)
        => db.Set<TimePolicy>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);

    public async Task<IReadOnlyList<TimePolicy>> ListWithReminderEnabledAsync(CancellationToken ct = default)
        => await db.Set<TimePolicy>().IgnoreQueryFilters()
            .Where(x => x.MissingTimeReminderEnabled)
            .ToListAsync(ct);
}

internal sealed class TimeTagStore(PlanvexaDbContext db) : ITimeTagStore
{
    public void Add(TimeTag tag) => db.Set<TimeTag>().Add(tag);

    public Task<TimeTag?> FindByNameAsync(Guid workspaceId, string name, CancellationToken ct = default)
        => db.Set<TimeTag>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Name.ToLower() == name.Trim().ToLower(), ct);

    public async Task<IReadOnlyList<TimeTag>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<TimeTag>().Where(x => x.WorkspaceId == workspaceId).OrderBy(x => x.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> ExistingTagIdsAsync(Guid workspaceId, IReadOnlyCollection<Guid> tagIds, CancellationToken ct = default)
        => await db.Set<TimeTag>()
            .Where(x => x.WorkspaceId == workspaceId && tagIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(ct);
}

internal sealed class BudgetStore(PlanvexaDbContext db) : IBudgetStore
{
    public void Add(Budget budget) => db.Set<Budget>().Add(budget);

    public void Remove(Budget budget) => db.Set<Budget>().Remove(budget);

    public Task<Budget?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
        => db.Set<Budget>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id, ct);

    public Task<Budget?> FindByScopeAsync(Guid workspaceId, BudgetScopeType scopeType, Guid scopeId, CancellationToken ct = default)
        => db.Set<Budget>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.ScopeType == scopeType && x.ScopeId == scopeId, ct);

    public async Task<IReadOnlyList<Budget>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<Budget>().Where(x => x.WorkspaceId == workspaceId).OrderBy(x => x.Name).ToListAsync(ct);
}

internal sealed class MemberRateStore(PlanvexaDbContext db) : IMemberRateStore
{
    public void Add(MemberRate rate) => db.Set<MemberRate>().Add(rate);

    public Task<MemberRate?> FindAsync(Guid workspaceId, Guid userId, Guid? projectId, CancellationToken ct = default)
        => db.Set<MemberRate>().FirstOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.UserId == userId && x.ProjectId == projectId, ct);

    public async Task<IReadOnlyList<MemberRate>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<MemberRate>().Where(x => x.WorkspaceId == workspaceId).ToListAsync(ct);
}

internal sealed class TimesheetStore(PlanvexaDbContext db) : ITimesheetStore
{
    public void Add(TimesheetPeriod period) => db.Set<TimesheetPeriod>().Add(period);

    public Task<TimesheetPeriod?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<TimesheetPeriod>().Include(p => p.Approvals).FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<TimesheetPeriod?> FindForUserWeekAsync(Guid workspaceId, Guid userId, DateTimeOffset periodStartUtc, CancellationToken ct = default)
        => db.Set<TimesheetPeriod>().Include(p => p.Approvals)
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId && x.PeriodStartUtc == periodStartUtc, ct);
}
