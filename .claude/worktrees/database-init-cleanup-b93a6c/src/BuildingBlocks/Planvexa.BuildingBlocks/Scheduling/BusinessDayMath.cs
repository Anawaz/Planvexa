namespace Planvexa.BuildingBlocks.Scheduling;

/// <summary>
/// Pure business-day date math shared by any module that needs "N business days from X"
/// automations date actions; reusable by future date-driven features). No I/O, no ambient state — the
/// working-days mask and holiday set are supplied by the caller (Planning module owns the actual
/// <c>WorkSchedule</c>/<c>Holiday</c> data behind the <c>IPlanningQueries</c> cross-module contract).
/// </summary>
public static class BusinessDayMath
{
    /// <summary>True if <paramref name="date"/> is a working day: its ISO day-of-week bit is set in
    /// <paramref name="workingDaysMask"/> (bit (isoDay-1), Mon=1..Sun=7) and it is not a holiday.</summary>
    public static bool IsWorkingDay(DateOnly date, int workingDaysMask, IReadOnlySet<DateOnly> holidays)
    {
        var iso = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
        return (workingDaysMask & (1 << (iso - 1))) != 0 && !holidays.Contains(date);
    }

    /// <summary>
    /// Adds <paramref name="businessDays"/> working days to <paramref name="start"/>, skipping non-working
    /// days per <paramref name="workingDaysMask"/> and <paramref name="holidays"/>. A non-positive
    /// <paramref name="businessDays"/> returns <paramref name="start"/> unchanged (no backward walk —
    /// callers needing "N business days before" negate the direction themselves by calling with a negated
    /// count once that need arises; not needed by this change's actions).
    /// </summary>
    public static DateOnly AddBusinessDays(DateOnly start, int businessDays, int workingDaysMask, IReadOnlySet<DateOnly> holidays)
    {
        if (workingDaysMask == 0)
        {
            // No working days configured at all: degrade to plain calendar-day addition rather than loop forever.
            return start.AddDays(businessDays);
        }

        var date = start;
        var remaining = businessDays;
        while (remaining > 0)
        {
            date = date.AddDays(1);
            if (IsWorkingDay(date, workingDaysMask, holidays))
            {
                remaining--;
            }
        }

        return date;
    }
}
