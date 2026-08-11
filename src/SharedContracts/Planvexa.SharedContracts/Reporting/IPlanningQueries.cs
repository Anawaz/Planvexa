namespace Planvexa.SharedContracts.Reporting;

/// <summary>Estimated effort (seconds) for a set of tasks.</summary>
public sealed record TaskEstimateInfo(Guid TaskId, long EstimateSeconds);

/// <summary>A sprint item's points, for sprint-progress dashboards.</summary>
public sealed record SprintPointRow(Guid SprintId, string SprintName, Guid TaskId, int Points);

/// <summary>A sprint's date range, for the burndown/burnup time series.</summary>
public sealed record SprintInfo(Guid SprintId, string SprintName, DateTimeOffset StartDate, DateTimeOffset EndDate);

/// <summary>
/// Read-side queries exposed by the Planning module (capacity + estimates) so the Reporting module
/// can compute workload/estimate-vs-actual without touching Planning tables directly. Runs under the
/// ambient tenant, scoped to a workspace.
/// </summary>
public interface IPlanningQueries
{
    /// <summary>Available capacity hours for a user over [fromUtc, toUtc), net of holidays and leave.</summary>
    Task<decimal> AvailableCapacityHoursAsync(Guid workspaceId, Guid userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>Estimates for the given task ids.</summary>
    Task<IReadOnlyList<TaskEstimateInfo>> EstimatesForTasksAsync(Guid workspaceId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default);

    /// <summary>Sprint items with points across all sprints in the workspace (for sprint-progress widgets).</summary>
    Task<IReadOnlyList<SprintPointRow>> SprintPointsAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>A sprint's date range, for the burndown/burnup widget. Null if not found.</summary>
    Task<SprintInfo?> SprintInfoAsync(Guid workspaceId, Guid sprintId, CancellationToken ct = default);

    /// <summary>
    /// Automations "add N business days" date action: adds <paramref name="businessDays"/>
    /// working days to <paramref name="fromUtc"/>'s calendar date, honoring the workspace's
    /// <c>WorkSchedule</c> (working-days mask) and configured <c>Holiday</c>s. Falls back to the default
    /// Mon–Fri schedule with no holidays when the workspace has not configured one. Time-of-day on
    /// <paramref name="fromUtc"/> is preserved on the result.
    /// </summary>
    Task<DateTimeOffset> AddBusinessDaysAsync(Guid workspaceId, DateTimeOffset fromUtc, int businessDays, CancellationToken ct = default);
}
