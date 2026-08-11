namespace Planvexa.Modules.TimeTracking.Domain;

/// <summary>
/// Pure, timezone-aware time arithmetic (ADR-0010). All inputs/outputs are UTC instants; local
/// calendar boundaries are computed with the supplied IANA timezone and are DST-safe. Money uses
/// decimal. Heavily unit-tested — no I/O, no ambient state.
/// </summary>
public static class TimeMath
{
    /// <summary>Whole-second duration between two instants (never negative).</summary>
    public static long DurationSeconds(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var seconds = (long)Math.Round((endUtc - startUtc).TotalSeconds, MidpointRounding.AwayFromZero);
        return seconds < 0 ? 0 : seconds;
    }

    /// <summary>
    /// Rounds a duration (seconds) up to the nearest rounding increment (minutes). A zero/negative
    /// increment returns the input unchanged. A non-zero duration always rounds to at least one unit.
    /// </summary>
    public static long RoundSecondsToIncrement(long seconds, int roundingMinutes)
    {
        if (roundingMinutes <= 0 || seconds <= 0)
        {
            return seconds < 0 ? 0 : seconds;
        }

        var increment = roundingMinutes * 60L;
        var units = (seconds + increment - 1) / increment; // ceil
        return units * increment;
    }

    /// <summary>Applies a minimum billable duration (seconds) to a non-zero duration.</summary>
    public static long ApplyMinimum(long seconds, long minimumSeconds)
    {
        if (seconds <= 0)
        {
            return 0;
        }

        return seconds < minimumSeconds ? minimumSeconds : seconds;
    }

    /// <summary>Hours as a decimal (seconds / 3600), rounded to 4 places for money math.</summary>
    public static decimal Hours(long seconds) => Math.Round(seconds / 3600m, 4, MidpointRounding.AwayFromZero);

    /// <summary>Money amount = hours × rate, rounded to 4 decimal places (decimal arithmetic only).</summary>
    public static decimal Amount(long seconds, decimal ratePerHour)
        => Math.Round(Hours(seconds) * ratePerHour, 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The UTC instant of the start of the local calendar day that contains <paramref name="instantUtc"/>
    /// in <paramref name="timeZone"/>. DST-safe: uses the timezone's own rules to map back to UTC.
    /// </summary>
    public static DateTimeOffset StartOfLocalDay(DateTimeOffset instantUtc, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(instantUtc, timeZone);
        var localMidnight = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return ToUtcFromUnspecifiedLocal(localMidnight, timeZone);
    }

    /// <summary>
    /// The UTC instant of the start of the local week containing <paramref name="instantUtc"/>, where
    /// <paramref name="weekStartsOn"/> is 0=Sunday … 6=Saturday. DST-safe.
    /// </summary>
    public static DateTimeOffset StartOfLocalWeek(DateTimeOffset instantUtc, TimeZoneInfo timeZone, int weekStartsOn)
    {
        var startOfDay = StartOfLocalDay(instantUtc, timeZone);
        var local = TimeZoneInfo.ConvertTime(startOfDay, timeZone);
        var currentDow = (int)local.DayOfWeek; // 0=Sunday
        var diff = (currentDow - weekStartsOn + 7) % 7;
        var weekStartLocalMidnight = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified).AddDays(-diff);
        return ToUtcFromUnspecifiedLocal(weekStartLocalMidnight, timeZone);
    }

    /// <summary>Resolves an IANA/Windows timezone id, falling back to UTC for unknown ids.</summary>
    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>
    /// Converts a wall-clock local time (Unspecified kind) to a UTC instant using the timezone rules,
    /// resolving DST gaps (spring-forward) by skipping into the offset after the gap, and ambiguous
    /// times (fall-back) by choosing the earlier (standard→daylight) interpretation deterministically.
    /// </summary>
    private static DateTimeOffset ToUtcFromUnspecifiedLocal(DateTime unspecifiedLocal, TimeZoneInfo timeZone)
    {
        if (timeZone.IsInvalidTime(unspecifiedLocal))
        {
            // Spring-forward gap: this wall time doesn't exist. Nudge forward by the DST delta (1h).
            unspecifiedLocal = unspecifiedLocal.AddHours(1);
        }

        var offset = timeZone.GetUtcOffset(unspecifiedLocal);
        return new DateTimeOffset(unspecifiedLocal, offset).ToUniversalTime();
    }
}
