namespace Planvexa.UnitTests.Goals;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Formulas;
using Planvexa.Modules.Goals.Domain;
using Planvexa.Modules.Reporting.Domain;
using Shouldly;
using Xunit;

/// <summary>Goal progress calculation (both numeric-target and linked-tasks-ratio styles).</summary>
public sealed class GoalProgressCalculatorTests
{
    [Theory]
    [InlineData(0, 200, 0)]
    [InlineData(50, 200, 25)]
    [InlineData(200, 200, 100)]
    [InlineData(400, 200, 200)]
    public void Numeric_percent_is_current_over_target(decimal current, decimal target, decimal expected)
        => GoalProgressCalculator.NumericPercent(current, target).ShouldBe(expected);

    [Fact]
    public void Numeric_percent_is_zero_when_target_is_non_positive()
        => GoalProgressCalculator.NumericPercent(50, 0).ShouldBe(0);

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 4, 0)]
    [InlineData(2, 4, 50)]
    [InlineData(4, 4, 100)]
    public void LinkedTasks_percent_is_completed_over_total(int completed, int total, decimal expected)
        => GoalProgressCalculator.LinkedTasksPercent(completed, total).ShouldBe(expected);

    [Fact]
    public void PercentComplete_dispatches_on_goal_target_type()
    {
        var numericGoal = Goal.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, "Revenue", null, Guid.CreateVersion7(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), GoalTargetType.Numeric, 200m, 50m, DateTimeOffset.UtcNow);
        GoalProgressCalculator.PercentComplete(numericGoal, completedLinkedTasks: 999, totalLinkedTasks: 999).ShouldBe(25m);

        var ratioGoal = Goal.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, "Ship features", null, Guid.CreateVersion7(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), GoalTargetType.LinkedTasksRatio, null, null, DateTimeOffset.UtcNow);
        GoalProgressCalculator.PercentComplete(ratioGoal, completedLinkedTasks: 3, totalLinkedTasks: 4).ShouldBe(75m);
    }
}

public sealed class GoalDomainTests
{
    [Fact]
    public void Numeric_goal_requires_a_positive_target_value()
    {
        Should.Throw<ValidationAppException>(() => Goal.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, "No target", null, Guid.CreateVersion7(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), GoalTargetType.Numeric, null, null, DateTimeOffset.UtcNow));

        Should.Throw<ValidationAppException>(() => Goal.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, "Zero target", null, Guid.CreateVersion7(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), GoalTargetType.Numeric, 0m, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void End_date_before_start_date_is_rejected()
        => Should.Throw<ValidationAppException>(() => Goal.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, "Backwards", null, Guid.CreateVersion7(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1), GoalTargetType.Numeric, 100m, null, DateTimeOffset.UtcNow));

    [Fact]
    public void Only_linked_tasks_ratio_goals_accept_task_links()
    {
        var numericGoal = Goal.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, "Revenue", null, Guid.CreateVersion7(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), GoalTargetType.Numeric, 200m, 0m, DateTimeOffset.UtcNow);

        Should.Throw<ValidationAppException>(() => numericGoal.LinkTask(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Linking_the_same_task_twice_is_idempotent()
    {
        var goal = Goal.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, "Ship it", null, Guid.CreateVersion7(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), GoalTargetType.LinkedTasksRatio, null, null, DateTimeOffset.UtcNow);
        var taskId = Guid.CreateVersion7();

        goal.LinkTask(Guid.CreateVersion7(), taskId, DateTimeOffset.UtcNow);
        goal.LinkTask(Guid.CreateVersion7(), taskId, DateTimeOffset.UtcNow);

        goal.LinkedTasks.Count.ShouldBe(1);
    }
}

/// <summary>The burndown/burnup day-by-day time-series computation.</summary>
public sealed class BurndownCalculatorTests
{
    [Fact]
    public void Remaining_decreases_as_tasks_complete_across_days()
    {
        var task1 = Guid.CreateVersion7();
        var task2 = Guid.CreateVersion7();
        var task3 = Guid.CreateVersion7();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 1, 3);

        var items = new List<(Guid, int)> { (task1, 3), (task2, 5), (task3, 2) };
        var completedAt = new Dictionary<Guid, DateTimeOffset?>
        {
            [task1] = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), // done day 1
            [task2] = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero), // done day 3
            [task3] = null, // never completed
        };

        var days = BurndownCalculator.Compute(items, completedAt, start, end);

        days.Count.ShouldBe(3);
        days[0].Completed.ShouldBe(3); // task1 done
        days[0].Remaining.ShouldBe(7); // 10 total - 3
        days[1].Completed.ShouldBe(3); // still just task1
        days[1].Remaining.ShouldBe(7);
        days[2].Completed.ShouldBe(8); // task1 + task2
        days[2].Remaining.ShouldBe(2); // only task3 left
    }

    [Fact]
    public void Task_with_no_completion_entry_never_counts_as_done()
    {
        var task = Guid.CreateVersion7();
        var days = BurndownCalculator.Compute(
            new List<(Guid, int)> { (task, 5) },
            new Dictionary<Guid, DateTimeOffset?>(),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));

        days.ShouldAllBe(d => d.Completed == 0 && d.Remaining == 5);
    }

    [Fact]
    public void Single_day_sprint_produces_exactly_one_point()
    {
        var day = new DateOnly(2026, 1, 1);
        var days = BurndownCalculator.Compute(
            new List<(Guid, int)>(), new Dictionary<Guid, DateTimeOffset?>(), day, day);

        days.Count.ShouldBe(1);
    }
}

/// <summary>The FormulaEngine's aggregate-function extension (SUM/COUNT/AVERAGE/MIN/MAX
/// over report rows) — reuses the FormulaParser/FormulaEvaluator rather than a second engine.</summary>
public sealed class AggregateFormulaEvaluatorTests
{
    private static readonly List<IReadOnlyDictionary<string, decimal>> Rows =
    [
        new Dictionary<string, decimal> { ["hours"] = 10m, ["tasks"] = 4m },
        new Dictionary<string, decimal> { ["hours"] = 20m, ["tasks"] = 6m },
        new Dictionary<string, decimal> { ["hours"] = 30m, ["tasks"] = 10m },
    ];

    [Theory]
    [InlineData("SUM({hours})", 60)]
    [InlineData("COUNT()", 3)]
    [InlineData("AVERAGE({hours})", 20)]
    [InlineData("MIN({hours})", 10)]
    [InlineData("MAX({hours})", 30)]
    public void Aggregate_functions_reduce_over_rows(string formula, decimal expected)
        => AggregateFormulaEvaluator.Evaluate(FormulaParser.Parse(formula), Rows).ShouldBe(expected);

    [Fact]
    public void Aggregates_compose_with_plain_arithmetic()
    {
        var node = FormulaParser.Parse("SUM({hours}) / COUNT()");
        AggregateFormulaEvaluator.Evaluate(node, Rows).ShouldBe(20m);
    }

    [Fact]
    public void Bare_field_reference_outside_an_aggregate_is_rejected()
    {
        var node = FormulaParser.Parse("{hours} + 1");
        Should.Throw<FormulaEvaluationException>(() => AggregateFormulaEvaluator.Evaluate(node, Rows));
    }

    [Fact]
    public void Aggregate_call_is_rejected_in_a_single_row_scalar_formula()
    {
        var node = FormulaParser.Parse("SUM({hours})");
        Should.Throw<FormulaEvaluationException>(() => FormulaEvaluator.Evaluate(node, new Dictionary<string, decimal> { ["hours"] = 1m }));
    }

    [Fact]
    public void Existing_scalar_formulas_still_parse_and_evaluate_unchanged()
    {
        var node = FormulaParser.Parse("({Field A} + {Field B}) * 2");
        var value = FormulaEvaluator.Evaluate(node, new Dictionary<string, decimal> { ["Field A"] = 3m, ["Field B"] = 4m });
        value.ShouldBe(14m);
    }
}

public sealed class ScheduledReportDomainTests
{
    [Fact]
    public void Daily_report_is_due_once_the_calendar_day_advances()
    {
        var report = ScheduledReport.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), new[] { "a@b.com" },
            ScheduledReportCadence.Daily, Guid.CreateVersion7(), new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

        report.IsDue(new DateTimeOffset(2026, 1, 1, 23, 0, 0, TimeSpan.Zero)).ShouldBeFalse();
        report.IsDue(new DateTimeOffset(2026, 1, 2, 0, 1, 0, TimeSpan.Zero)).ShouldBeTrue();
    }

    [Fact]
    public void Weekly_report_requires_seven_days_since_last_send()
    {
        var report = ScheduledReport.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), new[] { "a@b.com" },
            ScheduledReportCadence.Weekly, Guid.CreateVersion7(), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        report.IsDue(new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero)).ShouldBeFalse();
        report.IsDue(new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero)).ShouldBeTrue();
    }

    [Fact]
    public void At_least_one_valid_recipient_email_is_required()
        => Should.Throw<ValidationAppException>(() => ScheduledReport.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), new[] { "not-an-email", "  " },
            ScheduledReportCadence.Daily, Guid.CreateVersion7(), DateTimeOffset.UtcNow));

    [Fact]
    public void Disabled_report_is_never_due()
    {
        var report = ScheduledReport.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), new[] { "a@b.com" },
            ScheduledReportCadence.Daily, Guid.CreateVersion7(), new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        report.SetEnabled(false);

        report.IsDue(DateTimeOffset.UtcNow).ShouldBeFalse();
    }
}
