namespace Planvexa.UnitTests.Automations;

using Planvexa.BuildingBlocks.Scheduling;
using Shouldly;
using Xunit;

public sealed class BusinessDayMathTests
{
    // Mon-Fri mask: bits for iso days 1..5 set (Mon=1..Sun=7).
    private const int MonFriMask = 0b0011111;

    private static readonly IReadOnlySet<DateOnly> NoHolidays = new HashSet<DateOnly>();

    [Theory]
    [InlineData("2026-08-03", true)]  // Monday
    [InlineData("2026-08-07", true)]  // Friday
    [InlineData("2026-08-08", false)] // Saturday
    [InlineData("2026-08-09", false)] // Sunday
    public void IsWorkingDay_honors_the_working_days_mask(string date, bool expected)
        => BusinessDayMath.IsWorkingDay(DateOnly.Parse(date), MonFriMask, NoHolidays).ShouldBe(expected);

    [Fact]
    public void IsWorkingDay_treats_a_configured_holiday_as_non_working_even_on_a_weekday()
    {
        var monday = new DateOnly(2026, 8, 3);
        var holidays = new HashSet<DateOnly> { monday };
        BusinessDayMath.IsWorkingDay(monday, MonFriMask, holidays).ShouldBeFalse();
    }

    [Fact]
    public void AddBusinessDays_skips_weekends()
    {
        // Friday 2026-08-07 + 1 business day -> Monday 2026-08-10 (skips Sat/Sun).
        var friday = new DateOnly(2026, 8, 7);
        var result = BusinessDayMath.AddBusinessDays(friday, 1, MonFriMask, NoHolidays);
        result.ShouldBe(new DateOnly(2026, 8, 10));
    }

    [Fact]
    public void AddBusinessDays_skips_configured_holidays_too()
    {
        // Monday 2026-08-03 + 2 business days, with Tuesday 08-04 as a holiday -> Wed(1) skip Tue-holiday,
        // Thu(2) -> 2026-08-06.
        var monday = new DateOnly(2026, 8, 3);
        var holidays = new HashSet<DateOnly> { new DateOnly(2026, 8, 4) };
        var result = BusinessDayMath.AddBusinessDays(monday, 2, MonFriMask, holidays);
        result.ShouldBe(new DateOnly(2026, 8, 6));
    }

    [Fact]
    public void AddBusinessDays_zero_or_negative_returns_the_start_date_unchanged()
    {
        var monday = new DateOnly(2026, 8, 3);
        BusinessDayMath.AddBusinessDays(monday, 0, MonFriMask, NoHolidays).ShouldBe(monday);
        BusinessDayMath.AddBusinessDays(monday, -3, MonFriMask, NoHolidays).ShouldBe(monday);
    }

    [Fact]
    public void AddBusinessDays_over_a_multi_week_span_lands_on_the_expected_date()
    {
        // Monday 2026-08-03 + 10 business days = two full working weeks later -> Monday 2026-08-17.
        var monday = new DateOnly(2026, 8, 3);
        var result = BusinessDayMath.AddBusinessDays(monday, 10, MonFriMask, NoHolidays);
        result.ShouldBe(new DateOnly(2026, 8, 17));
    }

    [Fact]
    public void AddBusinessDays_degrades_to_calendar_days_when_no_working_days_are_configured()
    {
        var monday = new DateOnly(2026, 8, 3);
        BusinessDayMath.AddBusinessDays(monday, 5, workingDaysMask: 0, NoHolidays).ShouldBe(monday.AddDays(5));
    }
}
