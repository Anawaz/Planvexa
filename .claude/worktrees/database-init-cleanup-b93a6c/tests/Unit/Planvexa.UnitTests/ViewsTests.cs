namespace Planvexa.UnitTests.WorkManagement;

using Planvexa.Modules.WorkManagement.Application;
using Planvexa.Modules.WorkManagement.Domain;
using Shouldly;
using Xunit;

/// <summary>Standard CPM over a small dependency chain, worked by hand.</summary>
public sealed class CriticalPathCalculatorTests
{
    private static DateTimeOffset Day(int n) => new(2026, 1, 1 + n, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Single_chain_is_entirely_critical()
    {
        // A(0-2) -> B(2-5) -> C(5-6): every task is on the only path, so all three are critical.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var nodes = new[]
        {
            new CriticalPathCalculator.Node(a, Day(0), Day(2), []),
            new CriticalPathCalculator.Node(b, Day(2), Day(5), [a]),
            new CriticalPathCalculator.Node(c, Day(5), Day(6), [b]),
        };

        var critical = CriticalPathCalculator.Compute(nodes);

        critical.ShouldBe([a, b, c], ignoreOrder: true);
    }

    [Fact]
    public void Shorter_parallel_branch_has_slack_and_is_not_critical()
    {
        // A(0-1) branches into B(1-6, long) and C(1-2, short); both feed D(6-7).
        // The critical path is A -> B -> D (6 days); C has 4 days of slack and is NOT critical.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();
        var nodes = new[]
        {
            new CriticalPathCalculator.Node(a, Day(0), Day(1), []),
            new CriticalPathCalculator.Node(b, Day(1), Day(6), [a]),
            new CriticalPathCalculator.Node(c, Day(1), Day(2), [a]),
            new CriticalPathCalculator.Node(d, Day(6), Day(7), [b, c]),
        };

        var critical = CriticalPathCalculator.Compute(nodes);

        critical.ShouldBe([a, b, d], ignoreOrder: true);
        critical.ShouldNotContain(c);
    }

    [Fact]
    public void Undated_task_gets_a_nominal_one_day_duration()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var nodes = new[]
        {
            new CriticalPathCalculator.Node(a, null, null, []),
            new CriticalPathCalculator.Node(b, null, null, [a]),
        };

        var critical = CriticalPathCalculator.Compute(nodes);

        critical.ShouldBe([a, b], ignoreOrder: true);
    }

    [Fact]
    public void Dependency_cycle_is_excluded_instead_of_throwing()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var nodes = new[]
        {
            new CriticalPathCalculator.Node(a, Day(0), Day(1), [b]),
            new CriticalPathCalculator.Node(b, Day(0), Day(1), [a]),
        };

        var critical = CriticalPathCalculator.Compute(nodes);

        critical.ShouldBeEmpty();
    }

    [Fact]
    public void Empty_input_returns_empty_set()
    {
        CriticalPathCalculator.Compute([]).ShouldBeEmpty();
    }
}

/// <summary>Nested AND/OR filter-group evaluation over a TaskDto projection.</summary>
public sealed class TaskFilterEvaluatorTests
{
    private static readonly Guid StatusA = Guid.NewGuid();
    private static readonly Guid StatusB = Guid.NewGuid();
    private static readonly Guid Assignee1 = Guid.NewGuid();
    private static readonly Guid Assignee2 = Guid.NewGuid();

    private static TaskDto Task(Guid statusId, string priority = "Normal", bool isCompleted = false, Guid? assignee = null, string title = "Task")
        => new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 1, title, null,
            statusId, priority, null, null, false, isCompleted, 0,
            assignee is { } a ? [a] : [], [], false, null, null, []);

    [Fact]
    public void Null_group_matches_everything()
    {
        TaskFilterEvaluator.Matches(Task(StatusA), null).ShouldBeTrue();
    }

    [Fact]
    public void Empty_group_matches_everything()
    {
        var group = new FilterGroupDto(FilterLogic.And, [], []);
        TaskFilterEvaluator.Matches(Task(StatusA), group).ShouldBeTrue();
    }

    [Fact]
    public void And_group_requires_all_conditions()
    {
        var group = new FilterGroupDto(FilterLogic.And,
        [
            new FilterConditionDto("status", FilterOperator.Equals, StatusA.ToString()),
            new FilterConditionDto("priority", FilterOperator.Equals, "High"),
        ]);

        TaskFilterEvaluator.Matches(Task(StatusA, "High"), group).ShouldBeTrue();
        TaskFilterEvaluator.Matches(Task(StatusA, "Normal"), group).ShouldBeFalse();
        TaskFilterEvaluator.Matches(Task(StatusB, "High"), group).ShouldBeFalse();
    }

    [Fact]
    public void Or_group_requires_any_condition()
    {
        var group = new FilterGroupDto(FilterLogic.Or,
        [
            new FilterConditionDto("status", FilterOperator.Equals, StatusA.ToString()),
            new FilterConditionDto("priority", FilterOperator.Equals, "Urgent"),
        ]);

        TaskFilterEvaluator.Matches(Task(StatusA, "Normal"), group).ShouldBeTrue();
        TaskFilterEvaluator.Matches(Task(StatusB, "Urgent"), group).ShouldBeTrue();
        TaskFilterEvaluator.Matches(Task(StatusB, "Normal"), group).ShouldBeFalse();
    }

    [Fact]
    public void Nested_groups_combine_with_parent_logic()
    {
        // status = A AND (priority = Urgent OR assignee = Assignee1)
        var group = new FilterGroupDto(FilterLogic.And,
            Conditions: [new FilterConditionDto("status", FilterOperator.Equals, StatusA.ToString())],
            Groups:
            [
                new FilterGroupDto(FilterLogic.Or,
                [
                    new FilterConditionDto("priority", FilterOperator.Equals, "Urgent"),
                    new FilterConditionDto("assignee", FilterOperator.Equals, Assignee1.ToString()),
                ]),
            ]);

        TaskFilterEvaluator.Matches(Task(StatusA, "Urgent"), group).ShouldBeTrue();
        TaskFilterEvaluator.Matches(Task(StatusA, "Normal", assignee: Assignee1), group).ShouldBeTrue();
        TaskFilterEvaluator.Matches(Task(StatusA, "Normal", assignee: Assignee2), group).ShouldBeFalse();
        TaskFilterEvaluator.Matches(Task(StatusB, "Urgent"), group).ShouldBeFalse();
    }

    [Fact]
    public void IsEmpty_and_IsNotEmpty_operate_on_assignee_collection()
    {
        var unassigned = new FilterGroupDto(FilterLogic.And, [new FilterConditionDto("assignee", FilterOperator.IsEmpty, null)]);
        var assigned = new FilterGroupDto(FilterLogic.And, [new FilterConditionDto("assignee", FilterOperator.IsNotEmpty, null)]);

        TaskFilterEvaluator.Matches(Task(StatusA), unassigned).ShouldBeTrue();
        TaskFilterEvaluator.Matches(Task(StatusA), assigned).ShouldBeFalse();
        TaskFilterEvaluator.Matches(Task(StatusA, assignee: Assignee1), unassigned).ShouldBeFalse();
        TaskFilterEvaluator.Matches(Task(StatusA, assignee: Assignee1), assigned).ShouldBeTrue();
    }

    [Fact]
    public void Unknown_field_matches_everything_instead_of_failing_the_query()
    {
        var group = new FilterGroupDto(FilterLogic.And, [new FilterConditionDto("not-a-real-field", FilterOperator.Equals, "x")]);
        TaskFilterEvaluator.Matches(Task(StatusA), group).ShouldBeTrue();
    }

    [Fact]
    public void Title_contains_is_case_insensitive()
    {
        var group = new FilterGroupDto(FilterLogic.And, [new FilterConditionDto("title", FilterOperator.Contains, "urgent")]);
        TaskFilterEvaluator.Matches(Task(StatusA, title: "Fix URGENT bug"), group).ShouldBeTrue();
        TaskFilterEvaluator.Matches(Task(StatusA, title: "Regular task"), group).ShouldBeFalse();
    }
}
