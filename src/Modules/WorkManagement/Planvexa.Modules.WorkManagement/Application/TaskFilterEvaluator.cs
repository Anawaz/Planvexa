namespace Planvexa.Modules.WorkManagement.Application;

public enum FilterOperator
{
    Equals = 0,
    NotEquals = 1,
    Contains = 2,
    IsEmpty = 3,
    IsNotEmpty = 4,
    GreaterThan = 5,
    LessThan = 6,
    In = 7,
}

public enum FilterLogic
{
    And = 0,
    Or = 1,
}

/// <summary>One leaf condition in a nested filter tree. <see cref="Field"/> is one of "status",
/// "assignee", "tag", "priority", "title", "duedate", "startdate", "iscompleted" (case-insensitive).
/// <see cref="Value"/> is the operand as a string; for <see cref="FilterOperator.In"/> it's a
/// comma-separated list. Unknown fields never fail the query -- a leaf on an unrecognized field matches
/// everything, so a client typo narrows nothing rather than erroring the whole view.</summary>
public sealed record FilterConditionDto(string Field, FilterOperator Operator, string? Value);

/// <summary>One node of a nested AND/OR filter tree -- <see cref="Conditions"/> are this node's
/// own leaves, <see cref="Groups"/> are child groups combined by the same <see cref="Logic"/>. A group
/// with neither (both empty/null) matches everything, so an empty root filter is a no-op.</summary>
public sealed record FilterGroupDto(FilterLogic Logic, IReadOnlyList<FilterConditionDto>? Conditions = null, IReadOnlyList<FilterGroupDto>? Groups = null);

/// <summary>
/// Evaluates a nested AND/OR filter tree against an already-ACL-filtered <see cref="TaskDto"/>.
/// Pure/stateless (no I/O) so it's directly unit-testable -- see TaskFilterEvaluatorTests.
///
/// Deliberately operates in-memory over the DTO projection rather than translating to a SQL WHERE
/// clause: WorkItemService.ListByListAsync already loads and ACL-filters every task in a list in memory
/// (see the ponytail note in apps/web/src/lib/work/client.ts flagging the same choke point client-side),
/// so evaluating the filter tree at the same layer is consistent with the existing design, not a new
/// corner cut.
/// ponytail: in-memory evaluation over an already-loaded list; push into the EF query (WorkItemStore)
/// if a single list's task count makes this measurably slow.
/// </summary>
public static class TaskFilterEvaluator
{
    public static bool Matches(TaskDto task, FilterGroupDto? group)
    {
        if (group is null)
        {
            return true;
        }

        var results = new List<bool>();
        foreach (var condition in group.Conditions ?? [])
        {
            results.Add(MatchesCondition(task, condition));
        }

        foreach (var child in group.Groups ?? [])
        {
            results.Add(Matches(task, child));
        }

        if (results.Count == 0)
        {
            return true;
        }

        return group.Logic == FilterLogic.And ? results.All(r => r) : results.Any(r => r);
    }

    private static bool MatchesCondition(TaskDto task, FilterConditionDto condition) => condition.Field.ToLowerInvariant() switch
    {
        "status" => CompareGuid(task.StatusId, condition),
        "assignee" => CompareGuidCollection(task.AssigneeUserIds, condition),
        "tag" => CompareGuidCollection(task.TagIds, condition),
        "priority" => CompareText(task.Priority, condition),
        "title" => CompareText(task.Title, condition),
        "duedate" => CompareDate(task.DueDate, condition),
        "startdate" => CompareDate(task.StartDate, condition),
        "iscompleted" => condition.Operator == FilterOperator.Equals
            && bool.TryParse(condition.Value, out var expected) && task.IsCompleted == expected,
        _ => true,
    };

    private static bool CompareGuid(Guid actual, FilterConditionDto c) => c.Operator switch
    {
        FilterOperator.Equals => Guid.TryParse(c.Value, out var v) && actual == v,
        FilterOperator.NotEquals => !(Guid.TryParse(c.Value, out var v) && actual == v),
        FilterOperator.In => SplitGuids(c.Value).Contains(actual),
        FilterOperator.IsEmpty => actual == Guid.Empty,
        FilterOperator.IsNotEmpty => actual != Guid.Empty,
        _ => true,
    };

    private static bool CompareGuidCollection(IReadOnlyList<Guid> actual, FilterConditionDto c) => c.Operator switch
    {
        FilterOperator.IsEmpty => actual.Count == 0,
        FilterOperator.IsNotEmpty => actual.Count > 0,
        FilterOperator.Equals or FilterOperator.Contains => Guid.TryParse(c.Value, out var v) && actual.Contains(v),
        FilterOperator.NotEquals => !(Guid.TryParse(c.Value, out var v) && actual.Contains(v)),
        FilterOperator.In => SplitGuids(c.Value).Any(actual.Contains),
        _ => true,
    };

    private static bool CompareText(string actual, FilterConditionDto c) => c.Operator switch
    {
        FilterOperator.Equals => string.Equals(actual, c.Value, StringComparison.OrdinalIgnoreCase),
        FilterOperator.NotEquals => !string.Equals(actual, c.Value, StringComparison.OrdinalIgnoreCase),
        FilterOperator.Contains => c.Value is not null && actual.Contains(c.Value, StringComparison.OrdinalIgnoreCase),
        FilterOperator.IsEmpty => string.IsNullOrEmpty(actual),
        FilterOperator.IsNotEmpty => !string.IsNullOrEmpty(actual),
        FilterOperator.In => (c.Value ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(s => string.Equals(s, actual, StringComparison.OrdinalIgnoreCase)),
        _ => true,
    };

    private static bool CompareDate(DateTimeOffset? actual, FilterConditionDto c) => c.Operator switch
    {
        FilterOperator.IsEmpty => actual is null,
        FilterOperator.IsNotEmpty => actual is not null,
        FilterOperator.Equals => actual is not null && DateTimeOffset.TryParse(c.Value, out var v) && actual.Value.Date == v.Date,
        FilterOperator.NotEquals => !(actual is not null && DateTimeOffset.TryParse(c.Value, out var v) && actual.Value.Date == v.Date),
        FilterOperator.GreaterThan => actual is not null && DateTimeOffset.TryParse(c.Value, out var v) && actual.Value > v,
        FilterOperator.LessThan => actual is not null && DateTimeOffset.TryParse(c.Value, out var v) && actual.Value < v,
        _ => true,
    };

    private static IReadOnlyCollection<Guid> SplitGuids(string? value)
        => (value ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Guid.TryParse(s, out var v) ? v : (Guid?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();
}
