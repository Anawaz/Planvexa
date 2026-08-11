namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.TimeTracking.Domain;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Reporting;

/// <summary>
/// Implements the cross-module <see cref="ITimeReportingQueries"/> over TimeTracking tables. Money is
/// decimal; billable revenue = billable hours × billing rate. Relies on the ambient tenant query
/// filter for isolation. Only completed (ended) entries are counted.
/// </summary>
internal sealed class TimeReportingQueries(PlanvexaDbContext db) : ITimeReportingQueries
{
    public async Task<IReadOnlyList<LoggedTime>> LoggedByUserAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var entries = await db.Set<TimeEntry>()
            .Where(e => e.WorkspaceId == workspaceId && e.EndedAtUtc != null
                && e.StartedAtUtc >= fromUtc && e.StartedAtUtc < toUtc)
            .Select(e => new { e.UserId, e.DurationSeconds, e.IsBillable, e.BillingRate, e.CostRate })
            .ToListAsync(ct);

        return entries
            .GroupBy(e => e.UserId)
            .Select(g => new LoggedTime(
                g.Key,
                g.Sum(x => x.DurationSeconds),
                g.Where(x => x.IsBillable).Sum(x => x.DurationSeconds),
                g.Where(x => x.IsBillable).Sum(x => Money(x.DurationSeconds, x.BillingRate)),
                g.Sum(x => Money(x.DurationSeconds, x.CostRate))))
            .ToList();
    }

    public async Task<long> LoggedSecondsForUserAsync(Guid workspaceId, Guid userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
        => await db.Set<TimeEntry>()
            .Where(e => e.WorkspaceId == workspaceId && e.UserId == userId && e.EndedAtUtc != null
                && e.StartedAtUtc >= fromUtc && e.StartedAtUtc < toUtc)
            .SumAsync(e => (long?)e.DurationSeconds, ct) ?? 0L;

    public async Task<IReadOnlyDictionary<Guid, long>> LoggedSecondsByTaskAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var rows = await db.Set<TimeEntry>()
            .Where(e => e.WorkspaceId == workspaceId && e.TaskId != null && e.EndedAtUtc != null
                && e.StartedAtUtc >= fromUtc && e.StartedAtUtc < toUtc)
            .GroupBy(e => e.TaskId!.Value)
            .Select(g => new { TaskId = g.Key, Seconds = g.Sum(x => x.DurationSeconds) })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.TaskId, r => r.Seconds);
    }

    public async Task<decimal> BillableRevenueAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var entries = await db.Set<TimeEntry>()
            .Where(e => e.WorkspaceId == workspaceId && e.IsBillable && e.EndedAtUtc != null
                && e.StartedAtUtc >= fromUtc && e.StartedAtUtc < toUtc)
            .Select(e => new { e.DurationSeconds, e.BillingRate })
            .ToListAsync(ct);

        return entries.Sum(e => Money(e.DurationSeconds, e.BillingRate));
    }

    /// <summary>
    /// Portfolio-level budget reporting reuses the Space-scoped <see cref="Budget"/>
    /// directly (Space is already Portfolio's rollup grain — see PortfolioSpaceRow) rather than a new,
    /// coarser Portfolio budget concept. Joins TimeEntry → WorkItem → Space here (both tables are reachable
    /// from Infrastructure, which owns the shared DbContext) since <see cref="ITimeReportingQueries"/>'s
    /// other methods key by task/user, not space.
    /// </summary>
    public async Task<IReadOnlyList<SpaceBudgetStatusRow>> SpaceBudgetStatusesAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var budgets = await db.Set<Budget>()
            .Where(b => b.WorkspaceId == workspaceId && b.ScopeType == BudgetScopeType.Space)
            .ToListAsync(ct);
        if (budgets.Count == 0)
        {
            return Array.Empty<SpaceBudgetStatusRow>();
        }

        var spaceIds = budgets.Select(b => b.ScopeId).ToList();
        var rows = await (
            from e in db.Set<TimeEntry>()
            join t in db.Set<WorkItem>() on e.TaskId equals t.Id
            where e.WorkspaceId == workspaceId && e.EndedAtUtc != null
                && e.StartedAtUtc >= fromUtc && e.StartedAtUtc < toUtc
                && spaceIds.Contains(t.SpaceId)
            select new { t.SpaceId, e.DurationSeconds, e.CostRate })
            .ToListAsync(ct);

        var bySpace = rows
            .GroupBy(r => r.SpaceId)
            .ToDictionary(g => g.Key, g => (Seconds: g.Sum(x => x.DurationSeconds), Cost: g.Sum(x => Money(x.DurationSeconds, x.CostRate))));

        return budgets.Select(b =>
        {
            var (seconds, cost) = bySpace.GetValueOrDefault(b.ScopeId);
            var status = BudgetCalculator.Compute(b, seconds, cost, revenue: 0m);
            return new SpaceBudgetStatusRow(b.ScopeId, b.Name, b.MonetaryCapAmount, b.TimeCapSeconds, status.Hours, status.Cost, status.MonetaryConsumedPercent, status.TimeConsumedPercent);
        }).ToList();
    }

    private static decimal Money(long seconds, decimal ratePerHour)
        => Math.Round(Math.Round(seconds / 3600m, 4, MidpointRounding.AwayFromZero) * ratePerHour, 4, MidpointRounding.AwayFromZero);
}
