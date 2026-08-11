namespace Planvexa.UnitTests.WorkManagement;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Formulas;
using Planvexa.Modules.WorkManagement.Domain;
using Shouldly;
using Xunit;

/// <summary>The hand-rolled recursive-descent formula parser/evaluator.</summary>
public sealed class FormulaEngineTests
{
    [Theory]
    [InlineData("1 + 2", 3)]
    [InlineData("2 * 3 + 4", 10)]
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("-5 + 2", -3)]
    [InlineData("-(2 + 3)", -5)]
    [InlineData("2.5 * 2", 5)]
    public void Parses_and_evaluates_valid_expressions(string expression, decimal expected)
    {
        var node = FormulaParser.Parse(expression);
        FormulaEvaluator.Evaluate(node, new Dictionary<string, decimal>()).ShouldBe(expected);
    }

    [Fact]
    public void Evaluates_field_references_case_insensitively()
    {
        var node = FormulaParser.Parse("{Estimate} + {Buffer}");
        var values = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["estimate"] = 10, ["BUFFER"] = 5 };
        FormulaEvaluator.Evaluate(node, values).ShouldBe(15);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("1 + )")]
    [InlineData("1 $ 2")]
    [InlineData("{Unclosed")]
    [InlineData("{}")]
    [InlineData("1 2")]
    public void Rejects_malformed_expressions(string expression)
    {
        Should.Throw<FormulaParseException>(() => FormulaParser.Parse(expression));
    }

    [Fact]
    public void Evaluation_throws_for_unknown_field_reference()
    {
        var node = FormulaParser.Parse("{Missing} + 1");
        Should.Throw<FormulaEvaluationException>(() => FormulaEvaluator.Evaluate(node, new Dictionary<string, decimal>()));
    }

    [Fact]
    public void Evaluation_throws_on_division_by_zero()
    {
        var node = FormulaParser.Parse("1 / 0");
        Should.Throw<FormulaEvaluationException>(() => FormulaEvaluator.Evaluate(node, new Dictionary<string, decimal>()));
    }

    [Fact]
    public void CollectFieldRefs_finds_every_reference_once()
    {
        var node = FormulaParser.Parse("{A} + {B} * ({A} - {C})");
        var refs = FormulaEvaluator.CollectFieldRefs(node);
        refs.ShouldBe(new[] { "A", "B", "C" }, ignoreOrder: true);
    }
}

/// <summary>Save-time cycle detection and read-time evaluation ordering.</summary>
public sealed class CustomFieldDependencyGraphTests
{
    [Fact]
    public void No_cycle_for_a_simple_chain()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();

        // a depends on b, b depends on c.
        var nodes = new (Guid, IReadOnlyList<Guid>)[]
        {
            (a, new[] { b }),
            (b, new[] { c }),
            (c, Array.Empty<Guid>()),
        };

        CustomFieldDependencyGraph.HasCycle(nodes).ShouldBeFalse();
        CustomFieldDependencyGraph.TopologicalOrder(nodes).ShouldBe(new[] { c, b, a });
    }

    [Fact]
    public void Detects_a_direct_two_node_cycle()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        var nodes = new (Guid, IReadOnlyList<Guid>)[]
        {
            (a, new[] { b }),
            (b, new[] { a }),
        };

        CustomFieldDependencyGraph.HasCycle(nodes).ShouldBeTrue();
    }

    [Fact]
    public void Detects_a_self_reference_cycle()
    {
        var a = Guid.CreateVersion7();
        var nodes = new (Guid, IReadOnlyList<Guid>)[] { (a, new[] { a }) };
        CustomFieldDependencyGraph.HasCycle(nodes).ShouldBeTrue();
    }

    [Fact]
    public void Detects_a_transitive_three_node_cycle()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();

        // a -> b -> c -> a
        var nodes = new (Guid, IReadOnlyList<Guid>)[]
        {
            (a, new[] { b }),
            (b, new[] { c }),
            (c, new[] { a }),
        };

        CustomFieldDependencyGraph.HasCycle(nodes).ShouldBeTrue();
    }

    [Fact]
    public void Dependency_on_a_field_outside_the_node_set_is_not_a_cycle()
    {
        var a = Guid.CreateVersion7();
        var plainNumberField = Guid.CreateVersion7(); // not itself a Formula node

        var nodes = new (Guid, IReadOnlyList<Guid>)[] { (a, new[] { plainNumberField }) };
        CustomFieldDependencyGraph.HasCycle(nodes).ShouldBeFalse();
    }
}

/// <summary>Pure Sum/Count/Average/Min/Max aggregation math.</summary>
public sealed class RollupAggregatorTests
{
    [Fact]
    public void Sum_adds_target_values()
        => RollupAggregator.Aggregate(CustomFieldRollupFunction.Sum, 3, [1m, 2m, 3m]).ShouldBe(6m);

    [Fact]
    public void Sum_of_no_values_is_zero()
        => RollupAggregator.Aggregate(CustomFieldRollupFunction.Sum, 0, []).ShouldBe(0m);

    [Fact]
    public void Count_returns_visible_task_count_regardless_of_target_values()
        => RollupAggregator.Aggregate(CustomFieldRollupFunction.Count, 5, []).ShouldBe(5m);

    [Fact]
    public void Average_divides_by_value_count()
        => RollupAggregator.Aggregate(CustomFieldRollupFunction.Average, 4, [2m, 4m, 6m, 8m]).ShouldBe(5m);

    [Fact]
    public void Min_returns_smallest_value()
        => RollupAggregator.Aggregate(CustomFieldRollupFunction.Min, 3, [5m, 1m, 9m]).ShouldBe(1m);

    [Fact]
    public void Max_returns_largest_value()
        => RollupAggregator.Aggregate(CustomFieldRollupFunction.Max, 3, [5m, 1m, 9m]).ShouldBe(9m);

    [Fact]
    public void Average_with_no_values_throws()
        => Should.Throw<RollupEvaluationException>(() => RollupAggregator.Aggregate(CustomFieldRollupFunction.Average, 0, []));

    [Fact]
    public void Min_with_no_values_throws()
        => Should.Throw<RollupEvaluationException>(() => RollupAggregator.Aggregate(CustomFieldRollupFunction.Min, 0, []));

    [Fact]
    public void Max_with_no_values_throws()
        => Should.Throw<RollupEvaluationException>(() => RollupAggregator.Aggregate(CustomFieldRollupFunction.Max, 0, []));
}

/// <summary>CustomFieldDefinition.Create's per-type validation.</summary>
public sealed class CustomFieldDefinitionValidationTests
{
    private static readonly Guid WorkspaceId = Guid.CreateVersion7();

    private static CustomFieldDefinition CreateSimple(CustomFieldType type)
        => CustomFieldDefinition.Create(Guid.CreateVersion7(), WorkspaceId, CustomFieldScope.Workspace, null, "Field", type, false, 0);

    [Theory]
    [InlineData(CustomFieldType.User)]
    [InlineData(CustomFieldType.Team)]
    [InlineData(CustomFieldType.Phone)]
    [InlineData(CustomFieldType.Location)]
    [InlineData(CustomFieldType.Progress)]
    public void New_simple_types_can_be_created_without_extra_settings(CustomFieldType type)
        => CreateSimple(type).Type.ShouldBe(type);

    [Fact]
    public void Formula_field_requires_an_expression()
    {
        Should.Throw<ValidationAppException>(() => CustomFieldDefinition.Create(
            Guid.CreateVersion7(), WorkspaceId, CustomFieldScope.Workspace, null, "Total", CustomFieldType.Formula, false, 0));
    }

    [Fact]
    public void Formula_expression_on_a_non_formula_field_is_rejected()
    {
        Should.Throw<ValidationAppException>(() => CustomFieldDefinition.Create(
            Guid.CreateVersion7(), WorkspaceId, CustomFieldScope.Workspace, null, "Name", CustomFieldType.Text, false, 0,
            formulaExpression: "1 + 1"));
    }

    [Fact]
    public void Formula_field_with_expression_is_valid()
    {
        var definition = CustomFieldDefinition.Create(
            Guid.CreateVersion7(), WorkspaceId, CustomFieldScope.Workspace, null, "Total", CustomFieldType.Formula, false, 0,
            formulaExpression: "{A} + {B}", formulaDependencyIds: [Guid.CreateVersion7()]);
        definition.IsComputed.ShouldBeTrue();
        definition.FormulaDependencyIds.Count.ShouldBe(1);
    }

    [Fact]
    public void Rollup_field_requires_source_type_and_function()
    {
        Should.Throw<ValidationAppException>(() => CustomFieldDefinition.Create(
            Guid.CreateVersion7(), WorkspaceId, CustomFieldScope.Workspace, null, "Sum", CustomFieldType.Rollup, false, 0));
    }

    [Fact]
    public void Rollup_sourced_from_relationship_field_requires_source_field_id()
    {
        Should.Throw<ValidationAppException>(() => CustomFieldDefinition.Create(
            Guid.CreateVersion7(), WorkspaceId, CustomFieldScope.Workspace, null, "Sum", CustomFieldType.Rollup, false, 0,
            rollupSourceType: CustomFieldRollupSourceType.RelationshipField,
            rollupTargetFieldId: Guid.CreateVersion7(), rollupFunction: CustomFieldRollupFunction.Sum));
    }

    [Fact]
    public void Rollup_sourced_from_subtasks_rejects_a_source_field_id()
    {
        Should.Throw<ValidationAppException>(() => CustomFieldDefinition.Create(
            Guid.CreateVersion7(), WorkspaceId, CustomFieldScope.Workspace, null, "Sum", CustomFieldType.Rollup, false, 0,
            rollupSourceType: CustomFieldRollupSourceType.Subtasks, rollupSourceFieldId: Guid.CreateVersion7(),
            rollupTargetFieldId: Guid.CreateVersion7(), rollupFunction: CustomFieldRollupFunction.Sum));
    }

    [Fact]
    public void Rollup_with_sum_requires_a_target_field()
    {
        Should.Throw<ValidationAppException>(() => CustomFieldDefinition.Create(
            Guid.CreateVersion7(), WorkspaceId, CustomFieldScope.Workspace, null, "Sum", CustomFieldType.Rollup, false, 0,
            rollupSourceType: CustomFieldRollupSourceType.Subtasks, rollupFunction: CustomFieldRollupFunction.Sum));
    }

    [Fact]
    public void Rollup_count_rejects_a_target_field()
    {
        Should.Throw<ValidationAppException>(() => CustomFieldDefinition.Create(
            Guid.CreateVersion7(), WorkspaceId, CustomFieldScope.Workspace, null, "Count", CustomFieldType.Rollup, false, 0,
            rollupSourceType: CustomFieldRollupSourceType.Subtasks, rollupTargetFieldId: Guid.CreateVersion7(),
            rollupFunction: CustomFieldRollupFunction.Count));
    }

    [Fact]
    public void Valid_subtask_count_rollup_is_created()
    {
        var definition = CustomFieldDefinition.Create(
            Guid.CreateVersion7(), WorkspaceId, CustomFieldScope.Workspace, null, "Subtask count", CustomFieldType.Rollup, false, 0,
            rollupSourceType: CustomFieldRollupSourceType.Subtasks, rollupFunction: CustomFieldRollupFunction.Count);
        definition.IsComputed.ShouldBeTrue();
        definition.RollupFunction.ShouldBe(CustomFieldRollupFunction.Count);
    }

    [Fact]
    public void Rollup_settings_on_a_non_rollup_field_are_rejected()
    {
        Should.Throw<ValidationAppException>(() => CustomFieldDefinition.Create(
            Guid.CreateVersion7(), WorkspaceId, CustomFieldScope.Workspace, null, "Name", CustomFieldType.Number, false, 0,
            rollupFunction: CustomFieldRollupFunction.Count));
    }
}

/// <summary>CustomFieldValue's new User/Team typed projections.</summary>
public sealed class CustomFieldValueUserTeamTests
{
    [Fact]
    public void SetUser_clears_other_projections()
    {
        var value = CustomFieldValue.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        value.SetText("stale", DateTimeOffset.UtcNow);
        var userId = Guid.CreateVersion7();

        value.SetUser(userId, DateTimeOffset.UtcNow);

        value.UserValue.ShouldBe(userId);
        value.TextValue.ShouldBeNull();
    }

    [Fact]
    public void SetTeam_clears_other_projections()
    {
        var value = CustomFieldValue.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        value.SetNumber(42, DateTimeOffset.UtcNow);
        var teamId = Guid.CreateVersion7();

        value.SetTeam(teamId, DateTimeOffset.UtcNow);

        value.TeamValue.ShouldBe(teamId);
        value.NumberValue.ShouldBeNull();
    }
}
