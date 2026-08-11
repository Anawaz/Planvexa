namespace Planvexa.Modules.Planning.Application.Services;

using Planvexa.Modules.Planning.Authorization;
using Planvexa.Modules.Planning.Domain;
using Planvexa.SharedContracts.Reporting;

/// <summary>
/// Computes per-user workload for a workspace over a date range: capacity (from the working calendar
/// minus holidays and leave) vs scheduled effort (sum of estimates of assigned tasks) vs logged time.
/// Composes cross-module data only through contracts (never other modules' tables).
/// </summary>
public sealed class WorkloadService(
    PlanningServiceContext ctx,
    IWorkScheduleStore schedules,
    IHolidayStore holidays,
    ILeaveStore leave,
    IEstimateStore estimates,
    IWorkReportingQueries work,
    ITimeReportingQueries time)
    : PlanningServiceBase(ctx)
{
    public async Task<IReadOnlyList<WorkloadRowDto>> ComputeAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var fromDate = fromUtc.UtcDateTime.Date;
        var toDate = toUtc.UtcDateTime.Date;

        var schedule = await schedules.FindAsync(workspaceId, ct)
            ?? WorkSchedule.CreateDefault(NewId(), workspaceId);
        var holidayDates = (await holidays.ListInRangeAsync(workspaceId, fromDate, toDate, ct))
            .Select(h => h.DateUtc).ToList();

        var assignedByUser = await work.AssignedTaskIdsByUserAsync(workspaceId, ct);
        var loggedByUser = (await time.LoggedByUserAsync(workspaceId, fromUtc, toUtc, ct))
            .ToDictionary(l => l.UserId, l => l.TotalSeconds);

        // Union of users who either have assigned tasks or logged time in the range.
        var userIds = assignedByUser.Keys.Union(loggedByUser.Keys).Distinct().ToList();

        var rows = new List<WorkloadRowDto>(userIds.Count);
        foreach (var userId in userIds)
        {
            var userLeave = (await leave.ListForUserInRangeAsync(workspaceId, userId, fromDate, toDate, ct))
                .Select(l => (l.StartDate, l.EndDate))
                .ToList();

            var capacityHours = WorkloadEngine.AvailableCapacityHours(schedule, fromDate, toDate, holidayDates, userLeave);

            var taskIds = assignedByUser.TryGetValue(userId, out var ids) ? ids : Array.Empty<Guid>();
            long scheduledSeconds = 0;
            if (taskIds.Count > 0)
            {
                var taskEstimates = await estimates.ListByTaskIdsAsync(workspaceId, taskIds, ct);
                scheduledSeconds = taskEstimates.Sum(e => e.EstimateSeconds);
            }

            var loggedSeconds = loggedByUser.GetValueOrDefault(userId);
            var result = WorkloadEngine.Compute(capacityHours, scheduledSeconds, loggedSeconds);
            rows.Add(new WorkloadRowDto(userId, result.CapacityHours, result.ScheduledHours, result.LoggedHours, result.IsOverAllocated));
        }

        return rows.OrderByDescending(r => r.ScheduledHours).ToList();
    }
}
