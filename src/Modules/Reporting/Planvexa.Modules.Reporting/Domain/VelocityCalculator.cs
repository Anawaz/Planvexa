namespace Planvexa.Modules.Reporting.Domain;

/// <summary>
/// Pure "last N completed sprints" selection + rolling-average math for the Velocity widget, split out
/// of WidgetComputer.VelocityAsync the same way BurndownCalculator is split from BurndownAsync: the I/O
/// (sprint points, sprint dates, per-task completion) lives in WidgetComputer, this is a pure reduction
/// over data it already fetched.
/// </summary>
public static class VelocityCalculator
{
    public sealed record SprintVelocity(string SprintName, int CompletedPoints);

    public sealed record Result(IReadOnlyList<SprintVelocity> Sprints, decimal AveragePoints);

    /// <param name="sprints">Every sprint with its end date and already-computed completed points, any order.</param>
    /// <param name="nowUtc">A sprint counts as "completed" once its end date has passed (same date-based
    /// reasoning as BurndownAsync — no separate Status field needed).</param>
    /// <param name="sprintCount">How many of the most recent completed sprints to include.</param>
    public static Result Compute(
        IReadOnlyList<(Guid SprintId, string SprintName, DateTimeOffset EndDate, int CompletedPoints)> sprints,
        DateTimeOffset nowUtc, int sprintCount)
    {
        var lastN = sprints
            .Where(s => s.EndDate <= nowUtc)
            .OrderByDescending(s => s.EndDate)
            .Take(Math.Max(sprintCount, 0))
            .OrderBy(s => s.EndDate)
            .Select(s => new SprintVelocity(s.SprintName, s.CompletedPoints))
            .ToList();

        var average = lastN.Count == 0
            ? 0m
            : Math.Round(lastN.Average(s => (decimal)s.CompletedPoints), 1, MidpointRounding.AwayFromZero);

        return new Result(lastN, average);
    }
}
