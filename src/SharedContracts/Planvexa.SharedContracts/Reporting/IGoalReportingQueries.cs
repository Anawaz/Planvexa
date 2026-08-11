namespace Planvexa.SharedContracts.Reporting;

/// <summary>A Goal's percent-complete, for the GoalProgress dashboard widget.</summary>
public sealed record GoalProgressRow(Guid GoalId, string Name, decimal PercentComplete);

/// <summary>
/// Read-side queries exposed by the Goals module so the Reporting module can compose the
/// GoalProgress dashboard widget without touching Goals tables directly (AGENTS.md rule 7).
/// </summary>
public interface IGoalReportingQueries
{
    Task<IReadOnlyList<GoalProgressRow>> GoalProgressAsync(Guid workspaceId, CancellationToken ct = default);
}
