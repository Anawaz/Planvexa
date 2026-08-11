namespace Planvexa.Modules.Reporting.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Reporting.Authorization;
using Planvexa.SharedContracts.Reporting;

/// <summary>
/// Click-through from a Dashboard/Portfolio aggregate number to the underlying task
/// list that produced it (e.g. "12 overdue tasks" → those 12 tasks). SECURITY: always resolves candidate
/// task ids with the plain (unfiltered) IWorkReportingQueries methods, then ALWAYS narrows through
/// <see cref="IWorkReportingQueries.ReadableTaskCardsAsync"/> before returning — the same permission-aware
/// pattern Goals' linked-task display and the Rollup fields use — so a drill-down can never reveal a
/// private task's title to a viewer who could not otherwise read it. Reuses the existing task-card query
/// path rather than a new search index (AGENTS.md rule 16 / the design brief's explicit instruction).
/// </summary>
public sealed class DrillDownService(ReportingServiceContext ctx, IWorkReportingQueries work) : ReportingServiceBase(ctx)
{
    public async Task<IReadOnlyList<DrillDownTaskDto>> OverdueAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var ids = await work.OverdueTaskIdsAsync(workspaceId, Now, ct);
        return await ReadableAsync(workspaceId, ids, ct);
    }

    public async Task<IReadOnlyList<DrillDownTaskDto>> CompletedAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var ids = await work.CompletedTaskIdsAsync(workspaceId, fromUtc, toUtc, ct);
        return await ReadableAsync(workspaceId, ids, ct);
    }

    public async Task<IReadOnlyList<DrillDownTaskDto>> SpaceAsync(Guid spaceId, bool? completedOnly, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var ids = await work.SpaceTaskIdsAsync(workspaceId, spaceId, completedOnly, ct);
        return await ReadableAsync(workspaceId, ids, ct);
    }

    /// <summary>Click-through from a Workload row to the assigned tasks behind it. Same Manage gate as
    /// the Workload view itself (it surfaces another member's assignments, not just the caller's own).
    /// <paramref name="userId"/> == Guid.Empty means the Workload view's "Unassigned" bucket.</summary>
    public async Task<IReadOnlyList<DrillDownTaskDto>> AssigneeAsync(Guid userId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        if (userId == Guid.Empty)
        {
            var unassigned = await work.UnassignedTaskIdsAsync(workspaceId, ct);
            return await ReadableAsync(workspaceId, unassigned, ct);
        }

        var byUser = await work.AssignedTaskIdsByUserAsync(workspaceId, ct);
        var ids = byUser.GetValueOrDefault(userId, Array.Empty<Guid>());
        return await ReadableAsync(workspaceId, ids, ct);
    }

    private async Task<IReadOnlyList<DrillDownTaskDto>> ReadableAsync(Guid workspaceId, IReadOnlyList<Guid> taskIds, CancellationToken ct)
    {
        if (taskIds.Count == 0)
        {
            return Array.Empty<DrillDownTaskDto>();
        }

        var cards = await work.ReadableTaskCardsAsync(workspaceId, UserId, taskIds, ct);
        return cards.Select(c => new DrillDownTaskDto(c.TaskId, c.Title, c.StatusName, c.IsCompleted)).ToList();
    }
}
