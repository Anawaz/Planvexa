namespace Planvexa.Modules.Reporting.Application.Services;

using System.Text.Json;
using Planvexa.BuildingBlocks.Formulas;
using Planvexa.Modules.Reporting.Domain;
using Planvexa.SharedContracts.Reporting;

/// <summary>
/// Computes a widget's data series from cross-module query contracts only (never other modules'
/// tables). Pure composition over <see cref="IWorkReportingQueries"/>, <see cref="ITimeReportingQueries"/>
/// and <see cref="IPlanningQueries"/>. Given a widget type + a date range it returns a flat label/value
/// series suitable for charting. <paramref name="configJson"/> carries per-widget parameters
/// a plain date range can't ({"sprintId": "..."} for Burndown, {"formula": "SUM(hours)"} for CustomFormula).
/// </summary>
public sealed class WidgetComputer(
    IWorkReportingQueries work,
    ITimeReportingQueries time,
    IPlanningQueries planning,
    IGoalReportingQueries goals)
{
    public async Task<IReadOnlyList<SeriesPointDto>> ComputeAsync(
        Guid workspaceId, WidgetType type, DateTimeOffset fromUtc, DateTimeOffset toUtc, DateTimeOffset nowUtc, CancellationToken ct)
        => await ComputeAsync(workspaceId, type, fromUtc, toUtc, nowUtc, "{}", ct);

    public async Task<IReadOnlyList<SeriesPointDto>> ComputeAsync(
        Guid workspaceId, WidgetType type, DateTimeOffset fromUtc, DateTimeOffset toUtc, DateTimeOffset nowUtc, string configJson, CancellationToken ct)
        => type switch
        {
            WidgetType.TasksByStatus => await TasksByStatusAsync(workspaceId, ct),
            WidgetType.Overdue => Single("Overdue", await work.OverdueCountAsync(workspaceId, nowUtc, ct)),
            WidgetType.Completed => Single("Completed", await work.CompletedCountAsync(workspaceId, fromUtc, toUtc, ct)),
            WidgetType.TimeLogged => await TimeLoggedAsync(workspaceId, fromUtc, toUtc, ct),
            WidgetType.BillableTotals => Single("Billable", await time.BillableRevenueAsync(workspaceId, fromUtc, toUtc, ct)),
            WidgetType.Workload => await WorkloadAsync(workspaceId, ct),
            WidgetType.EstimateVsActual => await EstimateVsActualAsync(workspaceId, fromUtc, toUtc, ct),
            WidgetType.SprintProgress => await SprintProgressAsync(workspaceId, ct),
            WidgetType.PortfolioHealth => await PortfolioHealthAsync(workspaceId, ct),
            WidgetType.Burndown => await BurndownAsync(workspaceId, configJson, ct),
            WidgetType.CustomFormula => await CustomFormulaAsync(workspaceId, configJson, fromUtc, toUtc, ct),
            WidgetType.Velocity => await VelocityAsync(workspaceId, configJson, nowUtc, ct),
            WidgetType.TasksByAssignee => await TasksByAssigneeAsync(workspaceId, ct),
            WidgetType.TasksByPriority => await TasksByPriorityAsync(workspaceId, ct),
            WidgetType.CreatedVsCompleted => await CreatedVsCompletedAsync(workspaceId, fromUtc, toUtc, ct),
            WidgetType.GoalProgress => await GoalProgressAsync(workspaceId, ct),
            WidgetType.CustomFieldBreakdown => await CustomFieldBreakdownAsync(workspaceId, configJson, ct),
            _ => Array.Empty<SeriesPointDto>(),
        };

    private async Task<IReadOnlyList<SeriesPointDto>> TasksByStatusAsync(Guid workspaceId, CancellationToken ct)
    {
        var counts = await work.StatusCountsAsync(workspaceId, ct);
        return counts.Select(c => new SeriesPointDto(c.StatusName, c.Count)).ToList();
    }

    private async Task<IReadOnlyList<SeriesPointDto>> TimeLoggedAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var logged = await time.LoggedByUserAsync(workspaceId, fromUtc, toUtc, ct);
        var totalSeconds = logged.Sum(l => l.TotalSeconds);
        return Single("Logged hours", Hours(totalSeconds));
    }

    private async Task<IReadOnlyList<SeriesPointDto>> WorkloadAsync(Guid workspaceId, CancellationToken ct)
    {
        var assigned = await work.AssignedTaskIdsByUserAsync(workspaceId, ct);
        var taskIds = assigned.SelectMany(kv => kv.Value).Distinct().ToList();
        var estimates = await planning.EstimatesForTasksAsync(workspaceId, taskIds, ct);
        var secondsByTask = estimates.ToDictionary(e => e.TaskId, e => e.EstimateSeconds);

        var series = new List<SeriesPointDto>(assigned.Count);
        foreach (var (userId, userTaskIds) in assigned)
        {
            var scheduledHours = Hours(userTaskIds.Sum(id => secondsByTask.GetValueOrDefault(id)));
            series.Add(new SeriesPointDto(userId.ToString(), scheduledHours));
        }

        return series.OrderByDescending(s => s.Value).ToList();
    }

    private async Task<IReadOnlyList<SeriesPointDto>> EstimateVsActualAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var assigned = await work.AssignedTaskIdsByUserAsync(workspaceId, ct);
        var taskIds = assigned.SelectMany(kv => kv.Value).Distinct().ToList();
        var estimates = await planning.EstimatesForTasksAsync(workspaceId, taskIds, ct);
        var estimatedHours = Hours(estimates.Sum(e => e.EstimateSeconds));

        var logged = await time.LoggedByUserAsync(workspaceId, fromUtc, toUtc, ct);
        var actualHours = Hours(logged.Sum(l => l.TotalSeconds));

        return new List<SeriesPointDto>
        {
            new("Estimated", estimatedHours),
            new("Actual", actualHours),
        };
    }

    private async Task<IReadOnlyList<SeriesPointDto>> SprintProgressAsync(Guid workspaceId, CancellationToken ct)
    {
        var points = await planning.SprintPointsAsync(workspaceId, ct);
        if (points.Count == 0)
        {
            return Array.Empty<SeriesPointDto>();
        }

        var taskIds = points.Select(p => p.TaskId).Distinct().ToList();
        var cards = (await work.TaskCardsAsync(workspaceId, taskIds, ct)).ToDictionary(c => c.TaskId, c => c.IsCompleted);

        var series = new List<SeriesPointDto>();
        foreach (var group in points.GroupBy(p => (p.SprintId, p.SprintName)))
        {
            var total = group.Sum(p => p.Points);
            var done = group.Where(p => cards.GetValueOrDefault(p.TaskId)).Sum(p => p.Points);
            series.Add(new SeriesPointDto($"{group.Key.SprintName} — done", done));
            series.Add(new SeriesPointDto($"{group.Key.SprintName} — total", total));
        }

        return series;
    }

    private async Task<IReadOnlyList<SeriesPointDto>> PortfolioHealthAsync(Guid workspaceId, CancellationToken ct)
    {
        var rows = await work.PortfolioAsync(workspaceId, ct);
        return rows
            .Select(r => new SeriesPointDto(r.SpaceName, HealthPercent(r.TotalTasks, r.CompletedTasks)))
            .ToList();
    }

    public static decimal HealthPercent(int total, int completed)
        => total <= 0 ? 0m : Math.Round(completed * 100m / total, 1, MidpointRounding.AwayFromZero);

    /// <summary>Open task count per assignee (same raw-userId-as-label convention <see cref="WorkloadAsync"/>
    /// already uses — resolving it to a display name is a frontend/lookup concern, not this widget's).</summary>
    private async Task<IReadOnlyList<SeriesPointDto>> TasksByAssigneeAsync(Guid workspaceId, CancellationToken ct)
    {
        var counts = await work.TaskCountsByAssigneeAsync(workspaceId, ct);
        return counts
            .Select(kv => new SeriesPointDto(kv.Key.ToString(), kv.Value))
            .OrderByDescending(s => s.Value)
            .ToList();
    }

    private async Task<IReadOnlyList<SeriesPointDto>> TasksByPriorityAsync(Guid workspaceId, CancellationToken ct)
    {
        var counts = await work.PriorityCountsAsync(workspaceId, ct);
        return counts.Select(c => new SeriesPointDto(c.Priority, c.Count)).ToList();
    }

    /// <summary>Two-point comparison (mirrors <see cref="EstimateVsActualAsync"/>'s shape) of tasks
    /// created vs. completed within the reporting date range.</summary>
    private async Task<IReadOnlyList<SeriesPointDto>> CreatedVsCompletedAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var created = await work.CreatedCountAsync(workspaceId, fromUtc, toUtc, ct);
        var completed = await work.CompletedCountAsync(workspaceId, fromUtc, toUtc, ct);
        return new List<SeriesPointDto>
        {
            new("Created", created),
            new("Completed", completed),
        };
    }

    private async Task<IReadOnlyList<SeriesPointDto>> GoalProgressAsync(Guid workspaceId, CancellationToken ct)
    {
        var rows = await goals.GoalProgressAsync(workspaceId, ct);
        return rows.Select(r => new SeriesPointDto(r.Name, r.PercentComplete)).ToList();
    }

    /// <summary>Task count grouped by one custom field's stored values (e.g. a Dropdown field's options),
    /// via <see cref="IWorkReportingQueries.CustomFieldValueCountsAsync"/>. <paramref name="configJson"/>
    /// carries {"customFieldId": "..."} — same "empty until configured" convention as BurndownAsync's sprintId.</summary>
    private async Task<IReadOnlyList<SeriesPointDto>> CustomFieldBreakdownAsync(Guid workspaceId, string configJson, CancellationToken ct)
    {
        var definitionId = ReadGuidConfig(configJson, "customFieldId");
        if (definitionId is null)
        {
            return Array.Empty<SeriesPointDto>();
        }

        var counts = await work.CustomFieldValueCountsAsync(workspaceId, definitionId.Value, ct);
        return counts.Select(c => new SeriesPointDto(c.Label, c.Count)).ToList();
    }

    /// <summary>
    /// A real day-by-day burndown/burnup time series, not just SprintProgressAsync's
    /// single snapshot. Data-source approach: HISTORICAL RECONSTRUCTION from <c>WorkItem.CompletedAtUtc</c>
    /// (exposed cross-module as <see cref="IWorkReportingQueries.CompletedAtByTaskIdsAsync"/>) — chosen over
    /// a forward-looking projection because the domain already records the exact date each task was marked
    /// done, so "remaining points as of day D" is computed exactly (sum of a sprint item's points whose
    /// task's CompletedAtUtc is null or after D) rather than estimated. No new snapshot table is needed.
    /// </summary>
    private async Task<IReadOnlyList<SeriesPointDto>> BurndownAsync(Guid workspaceId, string configJson, CancellationToken ct)
    {
        var sprintId = ReadGuidConfig(configJson, "sprintId");
        if (sprintId is null)
        {
            return Array.Empty<SeriesPointDto>();
        }

        var sprint = await planning.SprintInfoAsync(workspaceId, sprintId.Value, ct);
        if (sprint is null)
        {
            return Array.Empty<SeriesPointDto>();
        }

        var items = (await planning.SprintPointsAsync(workspaceId, ct)).Where(p => p.SprintId == sprintId.Value).ToList();
        if (items.Count == 0)
        {
            return Array.Empty<SeriesPointDto>();
        }

        var taskIds = items.Select(i => i.TaskId).Distinct().ToList();
        var completedAt = await work.CompletedAtByTaskIdsAsync(workspaceId, taskIds, ct);

        var days = BurndownCalculator.Compute(
            items.Select(i => (i.TaskId, i.Points)).ToList(), completedAt,
            DateOnly.FromDateTime(sprint.StartDate.UtcDateTime), DateOnly.FromDateTime(sprint.EndDate.UtcDateTime));

        var series = new List<SeriesPointDto>();
        foreach (var day in days)
        {
            var label = day.Day.ToString("yyyy-MM-dd");
            series.Add(new SeriesPointDto($"{label} — remaining", day.Remaining));
            series.Add(new SeriesPointDto($"{label} — completed", day.Completed));
        }

        return series;
    }

    private const int DefaultVelocitySprintCount = 5;

    /// <summary>
    /// Completed story points per sprint across the last N completed sprints (a rolling velocity
    /// chart), plus the trailing average — the standard Scrum velocity report. Reuses the same
    /// contracts as SprintProgressAsync/BurndownAsync (planning.SprintPointsAsync + SprintInfoAsync,
    /// work.TaskCardsAsync) rather than adding a new cross-module query; see VelocityCalculator for
    /// the pure last-N/average math once this has fetched the per-sprint completed points and dates.
    /// <paramref name="configJson"/> optionally carries {"sprintCount": N}, default <see cref="DefaultVelocitySprintCount"/>.
    /// </summary>
    private async Task<IReadOnlyList<SeriesPointDto>> VelocityAsync(Guid workspaceId, string configJson, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var sprintCount = ReadIntConfig(configJson, "sprintCount") ?? DefaultVelocitySprintCount;
        var points = await planning.SprintPointsAsync(workspaceId, ct);
        if (points.Count == 0)
        {
            return Array.Empty<SeriesPointDto>();
        }

        var taskIds = points.Select(p => p.TaskId).Distinct().ToList();
        var cards = (await work.TaskCardsAsync(workspaceId, taskIds, ct)).ToDictionary(c => c.TaskId, c => c.IsCompleted);

        var sprints = new List<(Guid SprintId, string SprintName, DateTimeOffset EndDate, int CompletedPoints)>();
        foreach (var group in points.GroupBy(p => (p.SprintId, p.SprintName)))
        {
            var info = await planning.SprintInfoAsync(workspaceId, group.Key.SprintId, ct);
            if (info is null)
            {
                continue;
            }

            var done = group.Where(p => cards.GetValueOrDefault(p.TaskId)).Sum(p => p.Points);
            sprints.Add((group.Key.SprintId, group.Key.SprintName, info.EndDate, done));
        }

        var result = VelocityCalculator.Compute(sprints, nowUtc, sprintCount);
        if (result.Sprints.Count == 0)
        {
            return Array.Empty<SeriesPointDto>();
        }

        var series = result.Sprints.Select(s => new SeriesPointDto(s.SprintName, s.CompletedPoints)).ToList();
        series.Add(new SeriesPointDto("Average", result.AveragePoints));
        return series;
    }

    /// <summary>
    /// Evaluates a user-defined report formula (reusing the hand-rolled engine,
    /// extended with SUM/COUNT/AVERAGE/MIN/MAX aggregate functions — see
    /// <see cref="Planvexa.BuildingBlocks.Formulas.AggregateFormulaEvaluator"/>) over one row per Space
    /// with columns {hours, tasks, completed} — the same composition PortfolioService already does, so
    /// e.g. "SUM(hours) / COUNT(tasks)" computes average hours per task across the portfolio.
    /// </summary>
    private async Task<IReadOnlyList<SeriesPointDto>> CustomFormulaAsync(Guid workspaceId, string configJson, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var formula = ReadStringConfig(configJson, "formula");
        if (string.IsNullOrWhiteSpace(formula))
        {
            return Array.Empty<SeriesPointDto>();
        }

        var spaces = await work.PortfolioAsync(workspaceId, ct);
        var loggedByTask = await time.LoggedSecondsByTaskAsync(workspaceId, fromUtc, toUtc, ct);
        var taskToSpace = loggedByTask.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await work.SpaceIdByTaskAsync(workspaceId, loggedByTask.Keys.ToList(), ct);

        var loggedSecondsBySpace = new Dictionary<Guid, long>();
        foreach (var (taskId, seconds) in loggedByTask)
        {
            if (taskToSpace.TryGetValue(taskId, out var spaceId))
            {
                loggedSecondsBySpace[spaceId] = loggedSecondsBySpace.GetValueOrDefault(spaceId) + seconds;
            }
        }

        var rows = spaces.Select(s => (IReadOnlyDictionary<string, decimal>)new Dictionary<string, decimal>
        {
            ["hours"] = Hours(loggedSecondsBySpace.GetValueOrDefault(s.SpaceId)),
            ["tasks"] = s.TotalTasks,
            ["completed"] = s.CompletedTasks,
        }).ToList();

        decimal result;
        try
        {
            var node = FormulaParser.Parse(formula);
            result = AggregateFormulaEvaluator.Evaluate(node, rows);
        }
        catch (Exception ex) when (ex is FormulaParseException or FormulaEvaluationException)
        {
            return Single("Error", 0);
        }

        return Single("Result", result);
    }

    private static Guid? ReadGuidConfig(string configJson, string property)
    {
        var value = ReadStringConfig(configJson, property);
        return Guid.TryParse(value, out var guid) ? guid : null;
    }

    private static int? ReadIntConfig(string configJson, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson);
            return doc.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n)
                ? n
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadStringConfig(string configJson, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson);
            return doc.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal Hours(long seconds) => Math.Round(seconds / 3600m, 2, MidpointRounding.AwayFromZero);

    private static IReadOnlyList<SeriesPointDto> Single(string label, decimal value)
        => new List<SeriesPointDto> { new(label, value) };
}
