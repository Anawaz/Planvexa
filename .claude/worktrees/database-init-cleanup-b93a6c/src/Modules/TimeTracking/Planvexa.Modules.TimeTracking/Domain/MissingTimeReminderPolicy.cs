namespace Planvexa.Modules.TimeTracking.Domain;

/// <summary>
/// Pure logic for the missing-time reminder: when a cadence's period is over, and whether a
/// member's tracked time in that period falls short of the workspace's configured minimum. No I/O, no
/// ambient state -- mirrors <see cref="TimeMath"/>. Boundaries are UTC calendar day/week (the same
/// simplification <see cref="TimePolicy.LockDateUtc"/> and <see cref="TimesheetPeriod"/> already make;
/// entries carry their own IANA <see cref="TimeEntry.TimeZoneId"/>, but the workspace-level policy does
/// not store one).
/// ponytail: fixed UTC day/week boundaries and a fixed 23:00 UTC "period is over" cutoff, not
/// per-member-timezone-aware; revisit with each member's timezone if a workspace spread across zones
/// finds the reminder fires at an inconvenient local hour.
/// </summary>
public static class MissingTimeReminderPolicy
{
    private const int DueHourUtc = 23;

    /// <summary>The [start, end) of the cadence's current period containing <paramref name="nowUtc"/>.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) ResolvePeriod(
        MissingTimeReminderCadence cadence, DateTimeOffset nowUtc, int weekStartsOn)
    {
        var utc = TimeZoneInfo.Utc;
        if (cadence == MissingTimeReminderCadence.Weekly)
        {
            var start = TimeMath.StartOfLocalWeek(nowUtc, utc, weekStartsOn);
            return (start, start.AddDays(7));
        }

        var dayStart = TimeMath.StartOfLocalDay(nowUtc, utc);
        return (dayStart, dayStart.AddDays(1));
    }

    /// <summary>
    /// True once the current period is close enough to over to be worth reminding about (the last
    /// calendar day of the period, from <see cref="DueHourUtc"/> UTC onward). A background poll can call
    /// this on every tick; it stays true for the rest of the period, which is safe because the caller
    /// dedupes the actual notification per period (see MissingTimeReminderBackgroundService).
    /// </summary>
    public static bool IsPeriodDue(MissingTimeReminderCadence cadence, DateTimeOffset nowUtc, int weekStartsOn)
    {
        var (_, end) = ResolvePeriod(cadence, nowUtc, weekStartsOn);
        var lastDayStart = end.AddDays(-1);
        return nowUtc >= lastDayStart.AddHours(DueHourUtc);
    }

    /// <summary>A member is eligible for a reminder when their tracked time in the period is short of the minimum.</summary>
    public static bool IsEligible(long trackedSecondsInPeriod, long minimumSeconds)
        => trackedSecondsInPeriod < minimumSeconds;
}
