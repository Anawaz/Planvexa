namespace Planvexa.Modules.Planning.Application.Services;

using Planvexa.Modules.Planning.Domain;
using Planvexa.SharedContracts.Reporting;

/// <summary>
/// Implements the cross-module <see cref="IPlanningQueries"/> so the Reporting module can compose
/// workload / estimate-vs-actual widgets without touching Planning tables directly. Scoped to the
/// workspace passed in by the caller; isolation is enforced by the store's workspace query filter.
/// </summary>
public sealed class PlanningQueries(
    IWorkScheduleStore schedules,
    IHolidayStore holidays,
    ILeaveStore leave,
    IEstimateStore estimates,
    ISprintStore sprints)
    : IPlanningQueries
{
    public async Task<decimal> AvailableCapacityHoursAsync(Guid workspaceId, Guid userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var fromDate = fromUtc.UtcDateTime.Date;
        var toDate = toUtc.UtcDateTime.Date;

        var schedule = await schedules.FindAsync(workspaceId, ct)
            ?? WorkSchedule.CreateDefault(Guid.Empty, workspaceId);
        var holidayDates = (await holidays.ListInRangeAsync(workspaceId, fromDate, toDate, ct))
            .Select(h => h.DateUtc).ToList();
        var userLeave = (await leave.ListForUserInRangeAsync(workspaceId, userId, fromDate, toDate, ct))
            .Select(l => (l.StartDate, l.EndDate))
            .ToList();

        return WorkloadEngine.AvailableCapacityHours(schedule, fromDate, toDate, holidayDates, userLeave);
    }

    public async Task<IReadOnlyList<TaskEstimateInfo>> EstimatesForTasksAsync(Guid workspaceId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default)
    {
        if (taskIds.Count == 0)
        {
            return Array.Empty<TaskEstimateInfo>();
        }

        var list = await estimates.ListByTaskIdsAsync(workspaceId, taskIds, ct);
        return list.Select(e => new TaskEstimateInfo(e.TaskId, e.EstimateSeconds)).ToList();
    }

    public async Task<IReadOnlyList<SprintPointRow>> SprintPointsAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var list = await sprints.ListByWorkspaceAsync(workspaceId, ct);
        return list
            .SelectMany(s => s.Items.Select(i => new SprintPointRow(s.Id, s.Name, i.TaskId, i.Points ?? 0)))
            .ToList();
    }

    public async Task<SprintInfo?> SprintInfoAsync(Guid workspaceId, Guid sprintId, CancellationToken ct = default)
    {
        var sprint = await sprints.FindAsync(sprintId, ct);
        return sprint is null || sprint.WorkspaceId != workspaceId
            ? null
            : new SprintInfo(sprint.Id, sprint.Name, new DateTimeOffset(sprint.StartDate, TimeSpan.Zero), new DateTimeOffset(sprint.EndDate, TimeSpan.Zero));
    }

    public async Task<DateTimeOffset> AddBusinessDaysAsync(Guid workspaceId, DateTimeOffset fromUtc, int businessDays, CancellationToken ct = default)
    {
        var schedule = await schedules.FindAsync(workspaceId, ct) ?? WorkSchedule.CreateDefault(Guid.Empty, workspaceId);

        var fromDate = DateOnly.FromDateTime(fromUtc.UtcDateTime);
        // Wide-enough window to cover every plausible non-working day between the calendar-day span and
        // the working-day span (at most a handful of holidays per multi-week stretch in practice).
        var windowEnd = fromDate.AddDays(Math.Max(businessDays, 0) * 3 + 30);
        var holidayDates = (await holidays.ListInRangeAsync(workspaceId, fromDate.ToDateTime(TimeOnly.MinValue), windowEnd.ToDateTime(TimeOnly.MinValue), ct))
            .Select(h => DateOnly.FromDateTime(h.DateUtc))
            .ToHashSet();

        var result = Planvexa.BuildingBlocks.Scheduling.BusinessDayMath.AddBusinessDays(fromDate, businessDays, schedule.WorkingDaysMask, holidayDates);
        return new DateTimeOffset(result.ToDateTime(TimeOnly.FromTimeSpan(fromUtc.UtcDateTime.TimeOfDay)), TimeSpan.Zero);
    }
}
