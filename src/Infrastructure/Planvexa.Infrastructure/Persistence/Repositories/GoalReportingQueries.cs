namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Goals.Domain;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Reporting;

/// <summary>
/// Implements the cross-module <see cref="IGoalReportingQueries"/> over Goals + WorkManagement
/// tables. Lives in Infrastructure (which already owns the shared DbContext and references every
/// module), the same reuse pattern <see cref="WorkReportingQueries"/> already applies for its own
/// permission-aware methods (AGENTS.md rule 7). Reuses <see cref="GoalProgressCalculator"/> (the same
/// pure math <c>GoalService.ToDtoAsync</c> uses) so the widget and the Goals detail view never disagree
/// on a Goal's percent-complete.
/// </summary>
internal sealed class GoalReportingQueries(PlanvexaDbContext db) : IGoalReportingQueries
{
    public async Task<IReadOnlyList<GoalProgressRow>> GoalProgressAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var goals = await db.Set<Goal>()
            .Where(g => g.WorkspaceId == workspaceId)
            .Include(g => g.LinkedTasks)
            .ToListAsync(ct);
        if (goals.Count == 0)
        {
            return Array.Empty<GoalProgressRow>();
        }

        var taskIds = goals.SelectMany(g => g.LinkedTasks.Select(l => l.TaskId)).Distinct().ToList();
        var completedByTask = taskIds.Count == 0
            ? new Dictionary<Guid, bool>()
            : await db.Set<WorkItem>()
                .Where(t => taskIds.Contains(t.Id))
                .Select(t => new { t.Id, t.IsCompleted })
                .ToDictionaryAsync(t => t.Id, t => t.IsCompleted, ct);

        var rows = new List<GoalProgressRow>(goals.Count);
        foreach (var goal in goals)
        {
            var total = goal.LinkedTasks.Count;
            var completed = goal.LinkedTasks.Count(l => completedByTask.GetValueOrDefault(l.TaskId));
            var percent = GoalProgressCalculator.PercentComplete(goal, completed, total);
            rows.Add(new GoalProgressRow(goal.Id, goal.Name, percent));
        }

        return rows;
    }
}
