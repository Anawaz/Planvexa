namespace Planvexa.UnitTests.WorkManagement;

using Planvexa.Modules.WorkManagement.Domain;
using Shouldly;
using Xunit;

public sealed class PositioningTests
{
    [Fact]
    public void Append_adds_a_step_above_the_current_max()
    {
        Positioning.Append(null).ShouldBe(Positioning.Step);
        Positioning.Append(1024).ShouldBe(2048);
    }

    [Fact]
    public void Between_returns_the_midpoint_of_two_neighbours()
        => Positioning.Between(1000, 2000).ShouldBe(1500);

    [Fact]
    public void Between_handles_edges()
    {
        Positioning.Between(null, 1000).ShouldBeLessThan(1000);
        Positioning.Between(1000, null).ShouldBeGreaterThan(1000);
        Positioning.Between(null, null).ShouldBe(Positioning.Step);
    }
}

public sealed class RecurrenceTests
{
    [Theory]
    [InlineData(RecurrenceFrequency.Daily, 1, "2026-03-01T09:00:00Z", "2026-03-02T09:00:00Z")]
    [InlineData(RecurrenceFrequency.Weekly, 2, "2026-03-01T09:00:00Z", "2026-03-15T09:00:00Z")]
    [InlineData(RecurrenceFrequency.Monthly, 1, "2026-01-31T09:00:00Z", "2026-02-28T09:00:00Z")]
    [InlineData(RecurrenceFrequency.Yearly, 1, "2026-03-01T09:00:00Z", "2027-03-01T09:00:00Z")]
    public void Next_advances_by_frequency_and_interval(RecurrenceFrequency freq, int interval, string from, string expected)
    {
        var next = Recurrence.Next(DateTimeOffset.Parse(from), freq, interval);
        next.ShouldBe(DateTimeOffset.Parse(expected));
    }

    [Fact]
    public void OccurrenceKey_is_deterministic_for_the_same_occurrence()
    {
        var def = RecurringTaskDefinition.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "Weekly report", RecurrenceFrequency.Weekly, 1, "UTC", DateTimeOffset.Parse("2026-03-01T09:00:00Z"), Guid.CreateVersion7());

        var occurrence = DateTimeOffset.Parse("2026-03-08T09:00:00Z");
        def.OccurrenceKey(occurrence).ShouldBe(def.OccurrenceKey(occurrence));
        def.OccurrenceKey(occurrence).ShouldNotBe(def.OccurrenceKey(occurrence.AddDays(7)));
    }

    [Fact]
    public void AdvanceAfter_moves_next_run_forward()
    {
        var anchor = DateTimeOffset.Parse("2026-03-01T09:00:00Z");
        var def = RecurringTaskDefinition.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "Daily standup", RecurrenceFrequency.Daily, 1, "UTC", anchor, Guid.CreateVersion7());

        def.NextRunUtc.ShouldBe(anchor);
        def.AdvanceAfter(anchor, DateTimeOffset.UtcNow);
        def.NextRunUtc.ShouldBe(anchor.AddDays(1));
        def.LastGeneratedUtc.ShouldNotBeNull();
    }
}
