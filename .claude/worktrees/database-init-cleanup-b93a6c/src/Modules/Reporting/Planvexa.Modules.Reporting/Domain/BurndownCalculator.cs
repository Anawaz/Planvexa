namespace Planvexa.Modules.Reporting.Domain;

/// <summary>
/// Pure day-by-day burndown/burnup math, split out of WidgetComputer.BurndownAsync so
/// it is unit-testable without mocking the three cross-module query contracts (mirrors
/// RollupAggregator/BudgetCalculator's identical split — the I/O (fetching sprint points + each task's
/// completion date) lives in WidgetComputer, this reduces to a call here once it has that data).
/// </summary>
public static class BurndownCalculator
{
    public sealed record DayPoint(DateOnly Day, int Remaining, int Completed);

    /// <param name="items">Each sprint item's points, keyed by task id.</param>
    /// <param name="completedAtUtc">Each task's completion date (UTC), null if not completed. A task with
    /// no entry is treated as not completed.</param>
    /// <param name="startDay">Sprint start (UTC date, inclusive).</param>
    /// <param name="endDay">Sprint end (UTC date, inclusive).</param>
    public static IReadOnlyList<DayPoint> Compute(
        IReadOnlyList<(Guid TaskId, int Points)> items,
        IReadOnlyDictionary<Guid, DateTimeOffset?> completedAtUtc,
        DateOnly startDay, DateOnly endDay)
    {
        var totalPoints = items.Sum(i => i.Points);
        var result = new List<DayPoint>();

        for (var day = startDay; day <= endDay; day = day.AddDays(1))
        {
            var completedPoints = items
                .Where(i => completedAtUtc.TryGetValue(i.TaskId, out var c) && c is { } completed
                    && DateOnly.FromDateTime(completed.UtcDateTime) <= day)
                .Sum(i => i.Points);
            result.Add(new DayPoint(day, totalPoints - completedPoints, completedPoints));
        }

        return result;
    }
}
