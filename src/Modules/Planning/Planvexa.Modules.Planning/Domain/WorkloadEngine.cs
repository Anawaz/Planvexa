namespace Planvexa.Modules.Planning.Domain;

/// <summary>
/// Pure, deterministic capacity + workload arithmetic for the Planning module. No I/O, no ambient
/// state — heavily unit-tested. Capacity is computed from a working-day calendar minus holidays and
/// leave; workload compares scheduled effort (task estimates) and logged time against that capacity.
/// Day boundaries use UTC calendar dates (planning granularity is a day), which keeps the engine
/// timezone-stable for capacity accounting.
/// </summary>
public static class WorkloadEngine
{
    /// <summary>
    /// Number of working days in the inclusive UTC date range [fromDate, toDate], honouring the
    /// schedule's working-day mask and excluding holiday dates and any date covered by a leave entry.
    /// </summary>
    public static int WorkingDayCount(
        WorkSchedule schedule,
        DateTime fromDate,
        DateTime toDate,
        IReadOnlyCollection<DateTime> holidayDates,
        IReadOnlyCollection<(DateTime Start, DateTime End)> leaveRanges)
    {
        var from = fromDate.Date;
        var to = toDate.Date;
        if (to < from)
        {
            return 0;
        }

        var holidays = holidayDates.Select(d => d.Date).ToHashSet();
        var count = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (!schedule.IsWorkingDay(day.DayOfWeek))
            {
                continue;
            }

            if (holidays.Contains(day))
            {
                continue;
            }

            if (leaveRanges.Any(r => day >= r.Start.Date && day <= r.End.Date))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// Available capacity hours for a user over the inclusive range = working days × daily capacity.
    /// </summary>
    public static decimal AvailableCapacityHours(
        WorkSchedule schedule,
        DateTime fromDate,
        DateTime toDate,
        IReadOnlyCollection<DateTime> holidayDates,
        IReadOnlyCollection<(DateTime Start, DateTime End)> leaveRanges)
    {
        var workingDays = WorkingDayCount(schedule, fromDate, toDate, holidayDates, leaveRanges);
        return workingDays * schedule.DailyCapacityHours;
    }

    /// <summary>Hours as a decimal (seconds / 3600), rounded to 2 places for display/accounting.</summary>
    public static decimal Hours(long seconds) => Math.Round(seconds / 3600m, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Computes a workload row for a user: capacity vs scheduled effort (sum of estimate seconds) vs
    /// logged time. Over-allocated when scheduled effort strictly exceeds available capacity.
    /// </summary>
    public static WorkloadResult Compute(decimal capacityHours, long scheduledSeconds, long loggedSeconds)
    {
        var scheduledHours = Hours(scheduledSeconds);
        var loggedHours = Hours(loggedSeconds);
        var isOverAllocated = scheduledHours > capacityHours;
        return new WorkloadResult(capacityHours, scheduledHours, loggedHours, isOverAllocated);
    }
}

/// <summary>The result of a workload computation for one user over a date range.</summary>
public sealed record WorkloadResult(decimal CapacityHours, decimal ScheduledHours, decimal LoggedHours, bool IsOverAllocated);
