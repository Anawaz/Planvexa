namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Reporting;

// View DTOs returned to calendar/timeline/gantt clients. These are the SAME task records that task
// edits change — the views are projections over WorkManagement tasks, not a separate write model.
public sealed record CalendarTaskDto(Guid Id, string Title, DateTimeOffset? DueDate, bool IsCompleted, string Priority, IReadOnlyList<Guid> AssigneeUserIds);

// IsCritical (CPM), BaselineStartDate/BaselineDueDate (planned-vs-current) added for Gantt
// enhancements. Also reused as-is by the frontend Timeline view via the same /views/gantt
// endpoint -- Timeline renders the identical bars without dependency arrows/critical-path styling.
public sealed record GanttBarDto(
    Guid Id, string Title, DateTimeOffset? StartDate, DateTimeOffset? DueDate,
    bool IsMilestone, double Progress, IReadOnlyList<Guid> DependsOn, IReadOnlyList<Guid> AssigneeUserIds,
    bool IsCritical, DateTimeOffset? BaselineStartDate, DateTimeOffset? BaselineDueDate);

/// <summary>
/// Authorized read service for advanced views (calendar, gantt/timeline). Delegates data access to
/// the cross-module <see cref="IWorkReportingQueries"/> and enforces workspace read access.
///
/// SECURITY (found in review, fixed here): <see cref="IWorkReportingQueries"/> only scopes by
/// workspace -- it does NOT apply per-task privacy/ACL filtering. WorkManagementAuthorizer.EnsureRead
/// above is just the coarse role gate. Every result is re-filtered per task through
/// WorkServiceBase.CanReadAsync (same check WorkItemService.ListByListAsync and the Activity
/// feed already use) before being mapped to a DTO, so a private List/Task never leaks its title,
/// dates or assignees into Calendar, Gantt, or Timeline (which reuses GanttAsync's endpoint).
/// </summary>
public sealed class ViewQueryService(WorkServiceContext ctx, IWorkReportingQueries work, IWorkItemStore tasks)
    : WorkServiceBase(ctx)
{
    public async Task<IReadOnlyList<CalendarTaskDto>> CalendarAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? scopeSpaceId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var dated = await work.DatedTasksAsync(workspaceId, scopeSpaceId, fromUtc, toUtc, ct);
        var readableIds = await ReadableTaskIdsAsync(dated.Select(t => t.TaskId), ct);

        return dated
            .Where(t => readableIds.Contains(t.TaskId))
            .Select(t => new CalendarTaskDto(t.TaskId, t.Title, t.DueDate, t.IsCompleted, t.Priority, t.AssigneeUserIds))
            .ToList();
    }

    public async Task<IReadOnlyList<GanttBarDto>> GanttAsync(Guid spaceId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var allBars = await work.GanttTasksAsync(workspaceId, spaceId, ct);
        var readableIds = await ReadableTaskIdsAsync(allBars.Select(t => t.TaskId), ct);
        var bars = allBars.Where(t => readableIds.Contains(t.TaskId)).ToList();

        // Critical path over this space's dependency graph -- computed AFTER the ACL filter, so
        // a chain through a private task the caller cannot see cannot influence which visible tasks
        // are flagged critical (DependsOn ids pointing at a now-filtered-out task are simply ignored
        // by CriticalPathCalculator, same as any other dangling id).
        var criticalIds = CriticalPathCalculator.Compute(
            bars.Select(t => new CriticalPathCalculator.Node(t.TaskId, t.StartDate, t.DueDate, t.DependsOnTaskIds)).ToList());

        return bars
            .Select(t => new GanttBarDto(
                t.TaskId, t.Title, t.StartDate, t.DueDate, t.IsMilestone,
                t.IsCompleted ? 1.0 : 0.0,
                t.DependsOnTaskIds, t.AssigneeUserIds,
                criticalIds.Contains(t.TaskId),
                t.BaselineStartDate, t.BaselineDueDate))
            .ToList();
    }

    /// <summary>
    /// Batch-loads the WorkItem entities behind these ids in ONE query (not one per task), then runs
    /// the standard per-resource ACL/privacy check (CanReadAsync) per task -- that check itself is
    /// already the codebase's bounded (&lt;=3 hop ancestor probe, existence-checked before a full
    /// resolver call) per-resource evaluation, so this is the same cost class WorkItemService.
    /// ListByListAsync and WorkspaceActivityService already pay; there is no bulk ACL-evaluation
    /// method on IResourcePermissionQuery/IResourceHierarchyQuery to fold this into fewer round trips.
    /// ponytail: N ACL checks for N tasks in the date range/space -- acceptable at current view sizes
    /// (same tradeoff the rollup evaluation accepted); revisit only if a bulk
    /// IResourcePermissionQuery form is added for another reason.
    /// </summary>
    private async Task<HashSet<Guid>> ReadableTaskIdsAsync(IEnumerable<Guid> taskIds, CancellationToken ct)
    {
        var ids = taskIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var byId = await tasks.ListByIdsAsync(ids, ct);
        var result = new HashSet<Guid>();
        foreach (var task in byId)
        {
            if (await CanReadAsync(task, WorkResourceTypes.Task, ct))
            {
                result.Add(task.Id);
            }
        }

        return result;
    }
}
