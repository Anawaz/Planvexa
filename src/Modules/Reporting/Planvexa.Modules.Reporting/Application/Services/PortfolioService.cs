namespace Planvexa.Modules.Reporting.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Reporting.Application;
using Planvexa.Modules.Reporting.Authorization;
using Planvexa.Modules.Reporting.Domain;
using Planvexa.SharedContracts.Reporting;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Portfolio health report: per-space task rollups (total, completed, health%) plus logged hours,
/// Milestones (WorkItem.IsMilestone, not previously surfaced here), Risks (net new
/// risk register) and Budget status (reuses the Space-scoped Budget directly — see
/// SpaceBudgetStatusesAsync's doc comment), composed from the work + time query contracts + Reporting's
/// own Risk store. <see cref="GetAsync"/> (workspace-wide, kept for backward compatibility) is
/// Administrative (Admin+); the curated <see cref="Portfolio"/> CRUD + its own scoped
/// <see cref="GetReportAsync"/> follow Dashboard's ownership model instead (any member may create one,
/// owner or Admin may edit/delete, private ones are owner-only to read).
/// </summary>
public sealed class PortfolioService(
    ReportingServiceContext ctx,
    IPortfolioStore portfolios,
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
        return await BuildReportAsync(workspaceId, spaces, from, to, ct);
    }

    // ---- Curated Portfolio CRUD (named, owned, scoped to a chosen subset of Spaces) ----

    public async Task<IReadOnlyList<PortfolioSummaryDto>> ListPortfoliosAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var list = await portfolios.ListByWorkspaceAsync(workspaceId, ct);
        return list.Where(p => p.CanBeViewedBy(UserId)).Select(ToSummaryDto).ToList();
    }

    public async Task<PortfolioSummaryDto> CreatePortfolioAsync(CreatePortfolioCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureEdit((await AccessAsync(workspaceId, ct))?.Role);

        var ownerUserId = command.OwnerUserId is null || command.OwnerUserId == Guid.Empty ? UserId : command.OwnerUserId.Value;
        var portfolio = Portfolio.Create(NewId(), workspaceId, command.Name, ownerUserId, command.IsPrivate, command.Status, command.StartUtc, command.TargetEndUtc, Now);
        portfolio.ReplaceMembers(command.SpaceIds.Select(spaceId => (NewId(), spaceId)), Now);

        portfolios.Add(portfolio);
        Audit("reporting.portfolio.created", "Portfolio", portfolio.Id, new { portfolio.Name, portfolio.OwnerUserId });
        await SaveAsync(ct);
        return ToSummaryDto(portfolio);
    }

    public async Task<PortfolioSummaryDto> UpdatePortfolioAsync(Guid id, UpdatePortfolioCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        ReportingAuthorizer.EnsureRead(role);

        var portfolio = await portfolios.FindWithMembersAsync(id, ct)
            ?? throw new NotFoundException("Portfolio not found.");
        EnsureOwnerOrAdmin(portfolio, role);

        portfolio.Update(command.Name, command.OwnerUserId, command.IsPrivate, command.Status, command.StartUtc, command.TargetEndUtc, Now);
        if (command.SpaceIds is not null)
        {
            portfolio.ReplaceMembers(command.SpaceIds.Select(spaceId => (NewId(), spaceId)), Now);
        }

        Audit("reporting.portfolio.updated", "Portfolio", portfolio.Id, new { portfolio.Name });
        await SaveAsync(ct);
        return ToSummaryDto(portfolio);
    }

    public async Task DeletePortfolioAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        ReportingAuthorizer.EnsureRead(role);

        var portfolio = await portfolios.FindWithMembersAsync(id, ct)
            ?? throw new NotFoundException("Portfolio not found.");
        EnsureOwnerOrAdmin(portfolio, role);

        portfolios.Remove(portfolio);
        Audit("reporting.portfolio.deleted", "Portfolio", id);
        await SaveAsync(ct);
    }

    /// <summary>The Health/Progress/Milestones/Risks/Budget rollup, scoped to only this portfolio's
    /// curated Space ids -- same composition as <see cref="GetAsync"/>, just filtered down instead of
    /// covering every Space in the workspace.</summary>
    public async Task<IReadOnlyList<PortfolioRowDto>> GetReportAsync(Guid id, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var portfolio = await portfolios.FindWithMembersAsync(id, ct)
            ?? throw new NotFoundException("Portfolio not found.");
        portfolio.EnsureViewableBy(UserId);

        var to = toUtc ?? Now;
        var from = fromUtc ?? to.AddDays(-90);

        var spaceIds = portfolio.Members.Select(m => m.SpaceId).ToList();
        var spaces = spaceIds.Count == 0
            ? Array.Empty<PortfolioSpaceRow>()
            : await work.PortfolioAsync(workspaceId, spaceIds, ct);

        return await BuildReportAsync(workspaceId, spaces, from, to, ct);
    }

    private void EnsureOwnerOrAdmin(Portfolio portfolio, WorkspaceRole? role)
    {
        if (portfolio.OwnerUserId != UserId && role < WorkspaceRole.Admin)
        {
            throw new ForbiddenException("Only the portfolio owner or an administrator can modify it.");
        }
    }

    private static PortfolioSummaryDto ToSummaryDto(Portfolio p)
        => new(p.Id, p.Name, p.OwnerUserId, p.IsPrivate, p.Status, p.StartUtc, p.TargetEndUtc, p.Members.Select(m => m.SpaceId).ToList());

    private async Task<IReadOnlyList<PortfolioRowDto>> BuildReportAsync(
        Guid workspaceId, IReadOnlyList<PortfolioSpaceRow> spaces, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
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
