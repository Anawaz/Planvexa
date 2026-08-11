namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Formulas;

// A Formula field's expression, hand-parsed/evaluated (no `eval`, no reflection, no
// scripting-engine dependency — AGENTS.md rule 15/16). The pure grammar moved
// (FormulaNode/FormulaParser/FormulaEvaluator) into Planvexa.BuildingBlocks.Formulas (imported above) so
// the Reporting module can reuse the exact same engine for report-level formulas without a forbidden
// module-to-module reference (AGENTS.md rule 7). A field reference is `{Field Name}` (case-insensitive),
// resolved by the caller to either another custom field's numeric value or a built-in (currently only
// `{Priority}`, 0-4).

/// <summary>Raised when a Rollup field cannot be evaluated (as opposed to a Formula's
/// <see cref="FormulaEvaluationException"/>) — surfaced to the caller as CustomFieldValueDto.ComputedError.</summary>
public sealed class RollupEvaluationException(string message) : Exception(message);

/// <summary>
/// Pure aggregation math for a Rollup field, split out of CustomFieldService so it is
/// unit-testable without mocking stores/permissions — the I/O (gathering + permission-filtering source
/// tasks, reading their target-field values) lives in CustomFieldService.EvaluateRollupAsync, which reduces
/// to a call here once it has the visible-task count and the target field's numeric values.
/// </summary>
public static class RollupAggregator
{
    public static decimal Aggregate(CustomFieldRollupFunction function, int visibleTaskCount, IReadOnlyList<decimal> targetValues)
    {
        if (function == CustomFieldRollupFunction.Count)
        {
            return visibleTaskCount;
        }

        if (targetValues.Count == 0)
        {
            // Sum of nothing is meaningfully 0; average/min/max of nothing has no meaningful value.
            return function == CustomFieldRollupFunction.Sum
                ? 0
                : throw new RollupEvaluationException("No source values to aggregate.");
        }

        return function switch
        {
            CustomFieldRollupFunction.Sum => targetValues.Sum(),
            CustomFieldRollupFunction.Average => targetValues.Average(),
            CustomFieldRollupFunction.Min => targetValues.Min(),
            CustomFieldRollupFunction.Max => targetValues.Max(),
            _ => throw new RollupEvaluationException($"Unsupported rollup function '{function}'."),
        };
    }
}

/// <summary>
/// Cycle detection and evaluation ordering for Formula fields that reference other
/// Formula fields on the same task (Rollup fields deliberately cannot be a Formula dependency source
/// beyond their own node — see CustomFieldDefinition's doc comment on why RollupTargetFieldId is
/// restricted to simple stored types — so the only real cycle risk is Formula-references-Formula). Pure
/// (no I/O), so callers (CustomFieldService) resolve names to definition ids first, then hand this the
/// (id, dependsOnIds) edges.
/// </summary>
public static class CustomFieldDependencyGraph
{
    public static bool HasCycle(IReadOnlyList<(Guid Id, IReadOnlyList<Guid> DependsOn)> nodes)
    {
        var map = nodes.ToDictionary(n => n.Id, n => n.DependsOn);
        var state = new Dictionary<Guid, int>();
        foreach (var id in map.Keys)
        {
            if (Visit(id, map, state))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Visit(Guid id, Dictionary<Guid, IReadOnlyList<Guid>> map, Dictionary<Guid, int> state)
    {
        if (state.TryGetValue(id, out var s))
        {
            return s == 1;
        }

        state[id] = 1; // visiting
        if (map.TryGetValue(id, out var deps))
        {
            foreach (var dep in deps)
            {
                if (Visit(dep, map, state))
                {
                    return true;
                }
            }
        }

        state[id] = 2; // done
        return false;
    }

    /// <summary>Kahn-style topological order for read-time evaluation, so a Formula that references another
    /// computed field evaluates its dependency first. Throws if the graph has a cycle — callers must
    /// validate with <see cref="HasCycle"/> at save time so this never happens for saved data; this is a
    /// defense-in-depth check, not the primary validation gate.</summary>
    public static IReadOnlyList<Guid> TopologicalOrder(IReadOnlyList<(Guid Id, IReadOnlyList<Guid> DependsOn)> nodes)
    {
        var ids = nodes.Select(n => n.Id).ToHashSet();
        var deps = nodes.ToDictionary(n => n.Id, n => (IReadOnlyList<Guid>)n.DependsOn.Where(ids.Contains).ToList());
        var result = new List<Guid>();
        var visited = new HashSet<Guid>();
        var visiting = new HashSet<Guid>();

        void Visit(Guid id)
        {
            if (visited.Contains(id))
            {
                return;
            }

            if (!visiting.Add(id))
            {
                throw new InvalidOperationException("Cycle detected in custom field dependency graph.");
            }

            foreach (var dep in deps[id])
            {
                Visit(dep);
            }

            visiting.Remove(id);
            visited.Add(id);
            result.Add(id);
        }

        foreach (var id in ids)
        {
            Visit(id);
        }

        return result;
    }
}
