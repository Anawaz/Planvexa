namespace Planvexa.UnitTests.TimeTracking;

using Planvexa.Modules.TimeTracking.Domain;
using Shouldly;
using Xunit;

public sealed class TimeMathTests
{
    [Fact]
    public void DurationSeconds_is_computed_and_never_negative()
    {
        var start = DateTimeOffset.Parse("2026-03-01T09:00:00Z");
        TimeMath.DurationSeconds(start, start.AddMinutes(90)).ShouldBe(5400);
        TimeMath.DurationSeconds(start, start.AddSeconds(-10)).ShouldBe(0);
    }

    [Theory]
    [InlineData(60, 15, 900)]     // 1 min rounds up to 15 min
    [InlineData(900, 15, 900)]    // exactly 15 min stays
    [InlineData(901, 15, 1800)]   // just over rounds to 30
    [InlineData(3600, 0, 3600)]   // no rounding
    public void RoundSecondsToIncrement_ceilings_to_increment(long seconds, int minutes, long expected)
        => TimeMath.RoundSecondsToIncrement(seconds, minutes).ShouldBe(expected);

    [Fact]
    public void ApplyMinimum_raises_short_entries()
    {
        TimeMath.ApplyMinimum(120, 300).ShouldBe(300);
        TimeMath.ApplyMinimum(600, 300).ShouldBe(600);
        TimeMath.ApplyMinimum(0, 300).ShouldBe(0);
    }

    [Fact]
    public void Money_uses_decimal_and_rounds_to_four_places()
    {
        // 1h30m at 100.50/h = 150.75 exactly (decimal).
        TimeMath.Amount(5400, 100.50m).ShouldBe(150.75m);
        TimeMath.Hours(5400).ShouldBe(1.5m);
    }

    [Fact]
    public void StartOfLocalDay_is_dst_safe_for_spring_forward()
    {
        // US Eastern spring-forward 2026: clocks jump 2:00 -> 3:00 on 2026-03-08.
        var tz = TimeMath.ResolveTimeZone("America/New_York");
        // An instant on the morning of the DST day.
        var instant = DateTimeOffset.Parse("2026-03-08T12:00:00Z"); // ~07:00 local
        var startOfDay = TimeMath.StartOfLocalDay(instant, tz);

        // Local midnight that day is 05:00 UTC (EST, UTC-5) because the DST change happens at 02:00 local.
        startOfDay.ShouldBe(DateTimeOffset.Parse("2026-03-08T05:00:00Z"));
    }

    [Fact]
    public void StartOfLocalWeek_respects_week_start_day()
    {
        var tz = TimeMath.ResolveTimeZone("UTC");
        // 2026-03-04 is a Wednesday.
        var instant = DateTimeOffset.Parse("2026-03-04T15:00:00Z");

        // Week starting Monday (1) => 2026-03-02.
        TimeMath.StartOfLocalWeek(instant, tz, 1).ShouldBe(DateTimeOffset.Parse("2026-03-02T00:00:00Z"));
        // Week starting Sunday (0) => 2026-03-01.
        TimeMath.StartOfLocalWeek(instant, tz, 0).ShouldBe(DateTimeOffset.Parse("2026-03-01T00:00:00Z"));
    }

    [Fact]
    public void Duration_spanning_dst_gap_reflects_real_elapsed_time()
    {
        // A timer running across the US spring-forward: 01:30 EST to 03:30 EDT is only 1 real hour.
        var start = DateTimeOffset.Parse("2026-03-08T06:30:00Z"); // 01:30 EST
        var end = DateTimeOffset.Parse("2026-03-08T07:30:00Z");   // 03:30 EDT
        TimeMath.DurationSeconds(start, end).ShouldBe(3600);
    }

    [Fact]
    public void ResolveTimeZone_falls_back_to_utc_for_unknown()
        => TimeMath.ResolveTimeZone("Not/AZone").ShouldBe(TimeZoneInfo.Utc);
}

public sealed class TimeEntryDomainTests
{
    private static readonly Guid Ws = Guid.CreateVersion7();
    private static readonly Guid User = Guid.CreateVersion7();

    [Fact]
    public void StartTimer_creates_a_running_entry_with_no_duration()
    {
        var entry = TimeEntry.StartTimer(Guid.CreateVersion7(), Ws, User, null, DateTimeOffset.UtcNow, "UTC", true, 100m, 60m, null);
        entry.IsRunning.ShouldBeTrue();
        entry.DurationSeconds.ShouldBe(0);
        entry.EndedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void StartTimer_stores_the_idempotency_key_when_supplied()
    {
        var entry = TimeEntry.StartTimer(Guid.CreateVersion7(), Ws, User, null, DateTimeOffset.UtcNow, "UTC", true, 100m, 60m, null, "outbox-key-1");
        entry.IdempotencyKey.ShouldBe("outbox-key-1");
    }

    [Fact]
    public void Stop_computes_duration_from_server_timestamps()
    {
        var start = DateTimeOffset.Parse("2026-03-01T09:00:00Z");
        var entry = TimeEntry.StartTimer(Guid.CreateVersion7(), Ws, User, null, start, "UTC", true, 100m, 60m, null);

        entry.Stop(start.AddMinutes(45), "done");
        entry.IsRunning.ShouldBeFalse();
        entry.DurationSeconds.ShouldBe(2700);
        entry.Description.ShouldBe("done");
    }

    [Fact]
    public void Stopping_an_already_stopped_timer_throws()
    {
        var entry = TimeEntry.CreateManual(Guid.CreateVersion7(), Ws, User, null,
            DateTimeOffset.Parse("2026-03-01T09:00:00Z"), DateTimeOffset.Parse("2026-03-01T10:00:00Z"), "UTC", true, 0, 0, null, DateTimeOffset.UtcNow);

        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ConflictException>(() => entry.Stop(DateTimeOffset.UtcNow, null));
    }

    [Fact]
    public void Approved_entry_requires_a_reason_to_edit_and_returns_to_draft()
    {
        var entry = TimeEntry.CreateManual(Guid.CreateVersion7(), Ws, User, null,
            DateTimeOffset.Parse("2026-03-01T09:00:00Z"), DateTimeOffset.Parse("2026-03-01T10:00:00Z"), "UTC", true, 0, 0, null, DateTimeOffset.UtcNow);
        entry.Approve(Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ConflictException>(
            () => entry.AdjustTimes(null, DateTimeOffset.Parse("2026-03-01T10:30:00Z"), null, null, reason: null, DateTimeOffset.UtcNow));

        // With a reason it succeeds and the entry returns to Draft for re-approval.
        entry.AdjustTimes(null, DateTimeOffset.Parse("2026-03-01T10:30:00Z"), null, null, reason: "client correction", DateTimeOffset.UtcNow);
        entry.ApprovalStatus.ShouldBe(ApprovalStatus.Draft);
        entry.DurationSeconds.ShouldBe(5400);
    }

    [Fact]
    public void Locked_entry_cannot_be_edited_even_with_a_reason()
    {
        var entry = TimeEntry.CreateManual(Guid.CreateVersion7(), Ws, User, null,
            DateTimeOffset.Parse("2026-03-01T09:00:00Z"), DateTimeOffset.Parse("2026-03-01T10:00:00Z"), "UTC", true, 0, 0, null, DateTimeOffset.UtcNow);
        entry.Lock(DateTimeOffset.UtcNow);

        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ConflictException>(
            () => entry.AdjustTimes(null, DateTimeOffset.Parse("2026-03-01T11:00:00Z"), null, null, reason: "nope", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SplitAt_produces_two_contiguous_entries()
    {
        var start = DateTimeOffset.Parse("2026-03-01T09:00:00Z");
        var entry = TimeEntry.CreateManual(Guid.CreateVersion7(), Ws, User, null, start, start.AddHours(2), "UTC", true, 0, 0, null, DateTimeOffset.UtcNow);

        var remainder = entry.SplitAt(Guid.CreateVersion7(), start.AddHours(1), null, DateTimeOffset.UtcNow);

        entry.DurationSeconds.ShouldBe(3600);
        remainder.DurationSeconds.ShouldBe(3600);
        remainder.StartedAtUtc.ShouldBe(start.AddHours(1));
        remainder.EndedAtUtc.ShouldBe(start.AddHours(2));
    }

    [Fact]
    public void SplitAt_outside_the_entry_is_rejected()
    {
        var start = DateTimeOffset.Parse("2026-03-01T09:00:00Z");
        var entry = TimeEntry.CreateManual(Guid.CreateVersion7(), Ws, User, null, start, start.AddHours(1), "UTC", true, 0, 0, null, DateTimeOffset.UtcNow);

        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(
            () => entry.SplitAt(Guid.CreateVersion7(), start.AddHours(2), null, DateTimeOffset.UtcNow));
    }
}

public sealed class TimeAuthorizationTests
{
    [Theory]
    [InlineData(Planvexa.SharedContracts.Workspaces.WorkspaceRole.Guest, false, false)]
    [InlineData(Planvexa.SharedContracts.Workspaces.WorkspaceRole.Member, true, false)]
    [InlineData(Planvexa.SharedContracts.Workspaces.WorkspaceRole.Admin, true, true)]
    [InlineData(Planvexa.SharedContracts.Workspaces.WorkspaceRole.Owner, true, true)]
    public void Role_capabilities(Planvexa.SharedContracts.Workspaces.WorkspaceRole role, bool track, bool manage)
    {
        Planvexa.Modules.TimeTracking.Authorization.TimeAuthorizer.CanTrackOwn(role).ShouldBe(track);
        Planvexa.Modules.TimeTracking.Authorization.TimeAuthorizer.CanManage(role).ShouldBe(manage);
    }

    [Fact]
    public void Member_can_act_on_own_entry_but_not_others()
    {
        var me = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        Should.NotThrow(() => Planvexa.Modules.TimeTracking.Authorization.TimeAuthorizer.EnsureCanActOnEntry(
            Planvexa.SharedContracts.Workspaces.WorkspaceRole.Member, me, me));
        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ForbiddenException>(() =>
            Planvexa.Modules.TimeTracking.Authorization.TimeAuthorizer.EnsureCanActOnEntry(
                Planvexa.SharedContracts.Workspaces.WorkspaceRole.Member, other, me));
    }
}

/// <summary>Free-form tags on a TimeEntry (TimeEntry.SetTags mirrors WorkItem.SetTags).</summary>
public sealed class TimeEntryTagTests
{
    [Fact]
    public void SetTags_adds_and_removes_to_match_the_given_set()
    {
        var entry = TimeEntry.CreateManual(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null,
            DateTimeOffset.Parse("2026-03-01T09:00:00Z"), DateTimeOffset.Parse("2026-03-01T10:00:00Z"),
            "UTC", true, 0, 0, null, DateTimeOffset.UtcNow);

        var tagA = Guid.CreateVersion7();
        var tagB = Guid.CreateVersion7();
        var tagC = Guid.CreateVersion7();

        entry.SetTags(new[] { tagA, tagB }, Guid.CreateVersion7, DateTimeOffset.UtcNow);
        entry.Tags.Select(t => t.TagId).ShouldBe(new[] { tagA, tagB }, ignoreOrder: true);

        // Replacing with a different set drops A and keeps/adds only what's now listed.
        entry.SetTags(new[] { tagB, tagC }, Guid.CreateVersion7, DateTimeOffset.UtcNow);
        entry.Tags.Select(t => t.TagId).ShouldBe(new[] { tagB, tagC }, ignoreOrder: true);
    }

    [Fact]
    public void SetTags_is_idempotent_for_the_same_set()
    {
        var entry = TimeEntry.CreateManual(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null,
            DateTimeOffset.Parse("2026-03-01T09:00:00Z"), DateTimeOffset.Parse("2026-03-01T10:00:00Z"),
            "UTC", true, 0, 0, null, DateTimeOffset.UtcNow);

        var tagA = Guid.CreateVersion7();
        entry.SetTags(new[] { tagA }, Guid.CreateVersion7, DateTimeOffset.UtcNow);
        entry.SetTags(new[] { tagA }, Guid.CreateVersion7, DateTimeOffset.UtcNow);
        entry.Tags.Count.ShouldBe(1);
    }
}

/// <summary>Budget consumption + profitability math (pure, no I/O -- see Budget.cs doc comment).</summary>
public sealed class BudgetCalculatorTests
{
    private static Budget MakeBudget(decimal? monetaryCap, long? timeCap)
        => Budget.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), BudgetScopeType.List, Guid.CreateVersion7(), "Q1 delivery", monetaryCap, timeCap, DateTimeOffset.UtcNow);

    [Fact]
    public void Compute_reports_hours_cost_revenue_and_profit_exactly()
    {
        var budget = MakeBudget(1000m, null);
        // 10 hours tracked, cost 400, revenue 750 -> profit 350.
        var status = BudgetCalculator.Compute(budget, 10 * 3600, 400m, 750m);

        status.Hours.ShouldBe(10m);
        status.Cost.ShouldBe(400m);
        status.Revenue.ShouldBe(750m);
        status.Profit.ShouldBe(350m);
    }

    [Fact]
    public void Compute_measures_monetary_consumption_against_cost_not_revenue()
    {
        var budget = MakeBudget(500m, null);
        var status = BudgetCalculator.Compute(budget, 3600, cost: 250m, revenue: 900m);

        // 250 / 500 = 50%, not 900 / 500 (which would be over 100%).
        status.MonetaryConsumedPercent.ShouldBe(50m);
    }

    [Fact]
    public void Compute_reports_time_consumed_percent_against_the_time_cap()
    {
        var budget = MakeBudget(null, 20 * 3600);
        var status = BudgetCalculator.Compute(budget, 5 * 3600, cost: 0m, revenue: 0m);

        status.TimeConsumedPercent.ShouldBe(25m);
        status.MonetaryConsumedPercent.ShouldBeNull();
    }

    [Fact]
    public void Compute_returns_null_percent_when_the_corresponding_cap_is_not_set()
    {
        var budget = MakeBudget(500m, null); // no time cap
        var status = BudgetCalculator.Compute(budget, 3600, cost: 100m, revenue: 0m);
        status.TimeConsumedPercent.ShouldBeNull();
    }

    [Fact]
    public void Budget_requires_at_least_one_cap()
    {
        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() =>
            Budget.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), BudgetScopeType.Space, Guid.CreateVersion7(), "No caps", null, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Compute_can_exceed_100_percent_when_over_budget()
    {
        var budget = MakeBudget(100m, null);
        var status = BudgetCalculator.Compute(budget, 3600, cost: 150m, revenue: 0m);
        status.MonetaryConsumedPercent.ShouldBe(150m);
    }
}

/// <summary>Missing-time reminder period resolution + eligibility (pure, no I/O).</summary>
public sealed class MissingTimeReminderPolicyTests
{
    [Fact]
    public void ResolvePeriod_daily_is_the_utc_calendar_day()
    {
        var now = DateTimeOffset.Parse("2026-03-04T15:00:00Z"); // Wednesday
        var (start, end) = MissingTimeReminderPolicy.ResolvePeriod(MissingTimeReminderCadence.Daily, now, weekStartsOn: 1);

        start.ShouldBe(DateTimeOffset.Parse("2026-03-04T00:00:00Z"));
        end.ShouldBe(DateTimeOffset.Parse("2026-03-05T00:00:00Z"));
    }

    [Fact]
    public void ResolvePeriod_weekly_respects_week_start_day()
    {
        var now = DateTimeOffset.Parse("2026-03-04T15:00:00Z"); // Wednesday
        var (start, end) = MissingTimeReminderPolicy.ResolvePeriod(MissingTimeReminderCadence.Weekly, now, weekStartsOn: 1);

        start.ShouldBe(DateTimeOffset.Parse("2026-03-02T00:00:00Z")); // Monday
        end.ShouldBe(DateTimeOffset.Parse("2026-03-09T00:00:00Z"));
    }

    [Theory]
    [InlineData("2026-03-04T10:00:00Z", false)] // mid-day: not due yet
    [InlineData("2026-03-04T22:59:00Z", false)] // just before the cutoff
    [InlineData("2026-03-04T23:00:00Z", true)]  // at the cutoff
    [InlineData("2026-03-04T23:59:00Z", true)]  // rest of the day stays due
    public void IsPeriodDue_daily_fires_from_23_00_utc_onward(string nowText, bool expected)
        => MissingTimeReminderPolicy.IsPeriodDue(MissingTimeReminderCadence.Daily, DateTimeOffset.Parse(nowText), weekStartsOn: 1).ShouldBe(expected);

    [Fact]
    public void IsPeriodDue_weekly_fires_only_on_the_last_day_of_the_week()
    {
        // Week starts Monday, so the last day is Sunday (2026-03-08).
        MissingTimeReminderPolicy.IsPeriodDue(MissingTimeReminderCadence.Weekly, DateTimeOffset.Parse("2026-03-05T23:30:00Z"), weekStartsOn: 1).ShouldBeFalse();
        MissingTimeReminderPolicy.IsPeriodDue(MissingTimeReminderCadence.Weekly, DateTimeOffset.Parse("2026-03-08T23:30:00Z"), weekStartsOn: 1).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 3600, true)]
    [InlineData(1800, 3600, true)]
    [InlineData(3600, 3600, false)]
    [InlineData(7200, 3600, false)]
    public void IsEligible_flags_members_below_the_minimum(long trackedSeconds, long minimumSeconds, bool expected)
        => MissingTimeReminderPolicy.IsEligible(trackedSeconds, minimumSeconds).ShouldBe(expected);

    [Fact]
    public void IsEligible_a_disabled_minimum_of_zero_never_flags_anyone()
        => MissingTimeReminderPolicy.IsEligible(0, 0).ShouldBeFalse();
}
