namespace Planvexa.UnitTests.Planning;

using Planvexa.Modules.Planning.Domain;
using Planvexa.SharedContracts.Workspaces;
using Planvexa.Modules.Planning.Authorization;
using Shouldly;
using Xunit;

public sealed class WorkloadEngineTests
{
    private static WorkSchedule MonToFri8h()
    {
        // Default schedule is Mon–Fri, 8h/day.
        return WorkSchedule.CreateDefault(Guid.CreateVersion7(), Guid.CreateVersion7());
    }

    [Fact]
    public void WorkingDayCount_counts_only_working_weekdays()
    {
        var schedule = MonToFri8h();
        // 2026-03-02 (Mon) .. 2026-03-08 (Sun) => Mon-Fri = 5 working days.
        var from = new DateTime(2026, 3, 2);
        var to = new DateTime(2026, 3, 8);

        WorkloadEngine.WorkingDayCount(schedule, from, to, Array.Empty<DateTime>(), Array.Empty<(DateTime, DateTime)>())
            .ShouldBe(5);
    }

    [Fact]
    public void WorkingDayCount_excludes_holidays_and_leave()
    {
        var schedule = MonToFri8h();
        var from = new DateTime(2026, 3, 2); // Mon
        var to = new DateTime(2026, 3, 6);   // Fri (5 working days)

        var holidays = new[] { new DateTime(2026, 3, 4) }; // Wed is a holiday
        var leave = new[] { (new DateTime(2026, 3, 5), new DateTime(2026, 3, 5)) }; // Thu on leave

        // 5 weekdays - 1 holiday - 1 leave = 3.
        WorkloadEngine.WorkingDayCount(schedule, from, to, holidays, leave).ShouldBe(3);
    }

    [Fact]
    public void AvailableCapacityHours_is_working_days_times_daily_capacity()
    {
        var schedule = MonToFri8h();
        var from = new DateTime(2026, 3, 2);
        var to = new DateTime(2026, 3, 6); // 5 working days

        WorkloadEngine.AvailableCapacityHours(schedule, from, to, Array.Empty<DateTime>(), Array.Empty<(DateTime, DateTime)>())
            .ShouldBe(40m); // 5 * 8
    }

    [Fact]
    public void Custom_schedule_changes_capacity()
    {
        var schedule = MonToFri8h();
        schedule.Update(new[] { 1, 2, 3 }, 6m); // Mon-Wed, 6h/day
        var from = new DateTime(2026, 3, 2); // Mon
        var to = new DateTime(2026, 3, 6);   // Fri

        // Only Mon/Tue/Wed count => 3 * 6 = 18.
        WorkloadEngine.AvailableCapacityHours(schedule, from, to, Array.Empty<DateTime>(), Array.Empty<(DateTime, DateTime)>())
            .ShouldBe(18m);
    }

    [Fact]
    public void Compute_flags_over_allocation_when_scheduled_exceeds_capacity()
    {
        // Capacity 40h; scheduled 45h (162000s) => over-allocated.
        var result = WorkloadEngine.Compute(40m, scheduledSeconds: 45 * 3600, loggedSeconds: 10 * 3600);
        result.ScheduledHours.ShouldBe(45m);
        result.LoggedHours.ShouldBe(10m);
        result.IsOverAllocated.ShouldBeTrue();
    }

    [Fact]
    public void Compute_is_not_over_allocated_at_exact_capacity()
    {
        var result = WorkloadEngine.Compute(40m, scheduledSeconds: 40 * 3600, loggedSeconds: 0);
        result.IsOverAllocated.ShouldBeFalse();
    }

    [Fact]
    public void Hours_rounds_to_two_places()
        => WorkloadEngine.Hours(5400).ShouldBe(1.5m);
}

public sealed class SprintDomainTests
{
    [Fact]
    public void AddItem_is_idempotent_and_totals_points()
    {
        var sprint = Sprint.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "S1",
            DateTimeOffset.Parse("2026-03-01Z"), DateTimeOffset.Parse("2026-03-14Z"), Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        var taskA = Guid.CreateVersion7();
        var taskB = Guid.CreateVersion7();
        sprint.AddItem(Guid.CreateVersion7(), taskA, 3);
        sprint.AddItem(Guid.CreateVersion7(), taskB, 5);
        // Re-adding an existing task updates points instead of duplicating.
        sprint.AddItem(Guid.CreateVersion7(), taskA, 8);

        sprint.Items.Count.ShouldBe(2);
        sprint.TotalPoints().ShouldBe(13); // 8 + 5
    }

    [Fact]
    public void RemoveItem_removes_and_reports_missing()
    {
        var sprint = Sprint.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "S1",
            DateTimeOffset.Parse("2026-03-01Z"), DateTimeOffset.Parse("2026-03-14Z"), Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        var task = Guid.CreateVersion7();
        sprint.AddItem(Guid.CreateVersion7(), task, 2);

        sprint.RemoveItem(task).ShouldBeTrue();
        sprint.RemoveItem(task).ShouldBeFalse();
        sprint.TotalPoints().ShouldBe(0);
    }

    [Fact]
    public void Create_rejects_end_before_start()
        => Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() =>
            Sprint.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "bad",
                DateTimeOffset.Parse("2026-03-14Z"), DateTimeOffset.Parse("2026-03-01Z"), Guid.CreateVersion7(), DateTimeOffset.UtcNow));
}

public sealed class PlanningAuthorizerTests
{
    [Theory]
    [InlineData(WorkspaceRole.Guest, false)]
    [InlineData(WorkspaceRole.Member, false)]
    [InlineData(WorkspaceRole.Admin, true)]
    [InlineData(WorkspaceRole.Owner, true)]
    public void Manage_requires_admin(WorkspaceRole role, bool allowed)
        => PlanningAuthorizer.CanManage(role).ShouldBe(allowed);

    [Theory]
    [InlineData(WorkspaceRole.Guest, false)]
    [InlineData(WorkspaceRole.Member, true)]
    [InlineData(WorkspaceRole.Admin, true)]
    public void EditContent_requires_member(WorkspaceRole role, bool allowed)
        => PlanningAuthorizer.CanEditContent(role).ShouldBe(allowed);

    [Fact]
    public void EnsureManage_throws_for_member()
        => Should.Throw<Planvexa.BuildingBlocks.Exceptions.ForbiddenException>(() =>
            PlanningAuthorizer.EnsureManage(WorkspaceRole.Member));
}
