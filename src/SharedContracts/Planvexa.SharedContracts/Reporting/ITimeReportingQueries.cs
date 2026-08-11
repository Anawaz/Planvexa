namespace Planvexa.SharedContracts.Reporting;

/// <summary>Logged-time aggregates for a user in a date range.</summary>
public sealed record LoggedTime(Guid UserId, long TotalSeconds, long BillableSeconds, decimal Revenue, decimal Cost);

/// <summary>
/// A Space-scoped Budget's (<c>time.budgets</c>) cap + consumption for a date range —
/// Reuses this Space-grain concept directly for Portfolio-level budget reporting rather than
/// inventing a second, coarser budget entity: Space already IS Portfolio's rollup grain (see
/// PortfolioService/PortfolioSpaceRow), so no new concept is needed, only this read projection.
/// </summary>
public sealed record SpaceBudgetStatusRow(
    Guid SpaceId, string Name, decimal? MonetaryCapAmount, long? TimeCapSeconds,
    decimal Hours, decimal Cost, decimal? MonetaryConsumedPercent, decimal? TimeConsumedPercent);

/// <summary>
/// Read-side queries exposed by the TimeTracking module for dashboards/workload without touching
/// its tables directly (AGENTS.md rule 7). Runs under the ambient tenant, scoped to a workspace.
/// </summary>
public interface ITimeReportingQueries
{
    Task<IReadOnlyList<LoggedTime>> LoggedByUserAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    Task<long> LoggedSecondsForUserAsync(Guid workspaceId, Guid userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>Logged seconds keyed by task id in the range (entries with no task are excluded).</summary>
    Task<IReadOnlyDictionary<Guid, long>> LoggedSecondsByTaskAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>Total billable revenue for the workspace in the range (decimal money).</summary>
    Task<decimal> BillableRevenueAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>Every Space-scoped Budget's cap + consumption in the range, for Portfolio reporting.</summary>
    Task<IReadOnlyList<SpaceBudgetStatusRow>> SpaceBudgetStatusesAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}
