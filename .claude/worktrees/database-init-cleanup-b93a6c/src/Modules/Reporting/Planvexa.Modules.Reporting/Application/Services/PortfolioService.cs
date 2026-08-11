namespace Planvexa.Modules.Reporting.Application.Services;

using Planvexa.Modules.Reporting.Application;
using Planvexa.Modules.Reporting.Authorization;
using Planvexa.Modules.Reporting.Domain;
using Planvexa.SharedContracts.Reporting;

/// <summary>
/// Portfolio health report: per-space task rollups (total, completed, health%) plus logged hours,
/// Milestones (WorkItem.IsMilestone, not previously surfaced here), Risks (net new
/// risk register) and Budget status (reuses the Space-scoped Budget directly — see
/// SpaceBudgetStatusesAsync's doc comment), composed from the work + time query contracts + Reporting's
/// own Risk store. Administrative (Admin+).
/// </summary>
public sealed class PortfolioService(
    ReportingServiceContext ctx,
    IWorkReportingQueries work,
    ITimeReportingQueries time,
    IRiskStore riskStore)
    : ReportingServiceBase(ctx)
{
    public async Task<IReadOnlyList<PortfolioRowDto>> GetAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var to = toUtc ?? Now;
        var from = fromUtc ?? to.AddDays(-90);

        var spaces = await work.PortfolioAsync(workspaceId, ct);

        // Attribute logged time to spaces via task→space mapping (contract-only composition).
        var loggedByTask = await time.LoggedSecondsByTaskAsync(workspaceId, from, to, ct);
        var taskToSpace = loggedByTask.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await work.SpaceIdByTaskAsync(workspaceId, loggedByTask.Keys.ToList(), ct);

        var loggedHoursBySpace = new Dictionary<Guid, long>();
        foreach (var (taskId, seconds) in loggedByTask)
        {
            if (taskToSpace.TryGetValue(taskId, out var spaceId))
            {
                loggedHoursBySpace[spaceId] = loggedHoursBySpace.GetValueOrDefault(spaceId) + seconds;
            }
        }

        var milestonesBySpace = (await work.MilestonesAsync(workspaceId, ct))
            .GroupBy(m => m.SpaceId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<MilestoneDto>)g.Select(m => new MilestoneDto(m.TaskId, m.Title, m.DueDate, m.IsCompleted)).ToList());

        var risksBySpace = (await riskStore.ListByWorkspaceAsync(workspaceId, ct))
            .Where(r => r.ScopeType == RiskScopeType.Space)
            .GroupBy(r => r.ScopeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RiskDto>)g.Select(r => new RiskDto(r.Id, r.Title, r.Description, r.Severity, r.ScopeType, r.ScopeId, r.Status)).ToList());

        var budgetsBySpace = (await time.SpaceBudgetStatusesAsync(workspaceId, from, to, ct))
            .ToDictionary(b => b.SpaceId, b => new BudgetStatusDto(b.MonetaryCapAmount, b.TimeCapSeconds, b.Hours, b.Cost, b.MonetaryConsumedPercent, b.TimeConsumedPercent));

        return spaces
            .Select(s => new PortfolioRowDto(
                s.SpaceId.ToString(),
                s.SpaceName,
                s.TotalTasks,
                s.CompletedTasks,
                Hours(loggedHoursBySpace.GetValueOrDefault(s.SpaceId)),
                WidgetComputer.HealthPercent(s.TotalTasks, s.CompletedTasks),
                milestonesBySpace.GetValueOrDefault(s.SpaceId, Array.Empty<MilestoneDto>()),
                risksBySpace.GetValueOrDefault(s.SpaceId, Array.Empty<RiskDto>()),
                budgetsBySpace.GetValueOrDefault(s.SpaceId)))
            .OrderByDescending(r => r.TotalTasks)
            .ToList();
    }

    private static decimal Hours(long seconds) => Math.Round(seconds / 3600m, 2, MidpointRounding.AwayFromZero);
}
