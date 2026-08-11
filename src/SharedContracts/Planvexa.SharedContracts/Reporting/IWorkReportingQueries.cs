namespace Planvexa.SharedContracts.Reporting;

/// <summary>A status bucket count for a workspace (dashboard "tasks by status").</summary>
public sealed record StatusCount(Guid StatusId, string StatusName, string Category, int Count);

/// <summary>A dated task usable by calendar/gantt views and dashboards. BaselineStartDate/BaselineDueDate
/// are the last-captured "planned" snapshot, null until a caller sets one via
/// WorkItemService.SetBaselineAsync.</summary>
public sealed record DatedTask(
    Guid TaskId, Guid ListId, Guid SpaceId, string Title, DateTimeOffset? StartDate, DateTimeOffset? DueDate,
    bool IsMilestone, bool IsCompleted, string Priority, IReadOnlyList<Guid> AssigneeUserIds, IReadOnlyList<Guid> DependsOnTaskIds,
    DateTimeOffset? BaselineStartDate = null, DateTimeOffset? BaselineDueDate = null);

/// <summary>A task with its status, for board grouping (e.g. the sprint board).</summary>
public sealed record TaskCard(Guid TaskId, string Title, Guid StatusId, string StatusName, string StatusCategory, bool IsCompleted);

/// <summary>Per-space task rollup for portfolio reporting.</summary>
public sealed record PortfolioSpaceRow(Guid SpaceId, string SpaceName, int TotalTasks, int CompletedTasks);

/// <summary>A milestone task (<c>WorkItem.IsMilestone</c>) for portfolio surfacing.</summary>
public sealed record MilestoneRow(Guid TaskId, Guid SpaceId, string Title, DateTimeOffset? DueDate, bool IsCompleted);

/// <summary>A priority bucket count for a workspace (dashboard "tasks by priority").</summary>
public sealed record PriorityCount(string Priority, int Count);

/// <summary>A value bucket count for one custom field definition (dashboard "custom field breakdown"),
/// e.g. {Label: "In Review", Count: 4} for a Dropdown field's option.</summary>
public sealed record CustomFieldValueCount(string Label, int Count);

/// <summary>
/// Read-side queries exposed by the WorkManagement module so the Reporting module can compose
/// dashboards and view feeds without touching WorkManagement tables directly (AGENTS.md rule 7).
/// All run under the ambient tenant and are scoped to the current workspace.
/// </summary>
public interface IWorkReportingQueries
{
    Task<IReadOnlyList<StatusCount>> StatusCountsAsync(Guid workspaceId, CancellationToken ct = default);
    Task<int> OverdueCountAsync(Guid workspaceId, DateTimeOffset nowUtc, CancellationToken ct = default);
    Task<int> CompletedCountAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>Tasks with a due date in [fromUtc, toUtc) for calendar/dashboards.</summary>
    Task<IReadOnlyList<DatedTask>> DatedTasksAsync(Guid workspaceId, Guid? spaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>All non-deleted tasks in a space with dates + dependencies for the gantt view.</summary>
    Task<IReadOnlyList<DatedTask>> GanttTasksAsync(Guid workspaceId, Guid spaceId, CancellationToken ct = default);

    /// <summary>Task ids assigned to each user in the workspace (for workload scheduling).</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> AssignedTaskIdsByUserAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>Open (non-deleted, non-completed) task ids with zero assignees — the "Unassigned" bucket
    /// for workload scheduling/drill-down, paired with <see cref="AssignedTaskIdsByUserAsync"/>.</summary>
    Task<IReadOnlyList<Guid>> UnassignedTaskIdsAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>Task cards (title + status) for the given ids, for board grouping.</summary>
    Task<IReadOnlyList<TaskCard>> TaskCardsAsync(Guid workspaceId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default);

    /// <summary>Per-space task counts (total + completed) for portfolio reporting.</summary>
    Task<IReadOnlyList<PortfolioSpaceRow>> PortfolioAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>Same as <see cref="PortfolioAsync(Guid, CancellationToken)"/>, scoped down to only the
    /// given space ids -- a curated Portfolio's rollup (PortfolioService.GetReportAsync), instead of
    /// every Space in the workspace.</summary>
    Task<IReadOnlyList<PortfolioSpaceRow>> PortfolioAsync(Guid workspaceId, IReadOnlyCollection<Guid> spaceIds, CancellationToken ct = default);

    /// <summary>Maps each of the given task ids to its owning space id.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> SpaceIdByTaskAsync(Guid workspaceId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default);

    // ---- Goals/OKRs + reporting completeness ----

    /// <summary>Overdue task ids (for drill-down from the Overdue widget's count to its task list).</summary>
    Task<IReadOnlyList<Guid>> OverdueTaskIdsAsync(Guid workspaceId, DateTimeOffset nowUtc, CancellationToken ct = default);

    /// <summary>Task ids completed in [fromUtc, toUtc) (for drill-down from the Completed widget's count).</summary>
    Task<IReadOnlyList<Guid>> CompletedTaskIdsAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>Task ids in a space (for drill-down from a Portfolio row's total/completed count).</summary>
    Task<IReadOnlyList<Guid>> SpaceTaskIdsAsync(Guid workspaceId, Guid spaceId, bool? completedOnly, CancellationToken ct = default);

    /// <summary>
    /// SECURITY: the given task ids' cards, filtered down to only those <paramref name="userId"/> may
    /// read — the exact same per-resource authorization walk <c>WorkManagementAuthorizer.CanReadAsync</c>
    /// already applies inside WorkManagement (private tasks/lists/folders/spaces, ACL grants), reused here
    /// so a cross-module caller (Goals' linked-tasks display, Reporting's drill-down) can never leak a
    /// task's title/data the viewer could not otherwise see. Always use this (never
    /// <see cref="TaskCardsAsync"/>) when the result is displayed rather than merely counted/aggregated.
    /// </summary>
    Task<IReadOnlyList<TaskCard>> ReadableTaskCardsAsync(Guid workspaceId, Guid userId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default);

    /// <summary>When each of the given task ids was completed (null if not completed) — the burndown/burnup
    /// time-series' historical-reconstruction data source (<c>WorkItem.CompletedAtUtc</c>).</summary>
    Task<IReadOnlyDictionary<Guid, DateTimeOffset?>> CompletedAtByTaskIdsAsync(Guid workspaceId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default);

    /// <summary>Every milestone task (<c>WorkItem.IsMilestone</c>) in the workspace, for Portfolio surfacing.</summary>
    Task<IReadOnlyList<MilestoneRow>> MilestonesAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>Open (non-deleted, non-completed) task counts grouped by assignee user id, for the
    /// TasksByAssignee dashboard widget.</summary>
    Task<IReadOnlyDictionary<Guid, int>> TaskCountsByAssigneeAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>Task counts grouped by priority, for the TasksByPriority dashboard widget.</summary>
    Task<IReadOnlyList<PriorityCount>> PriorityCountsAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>Count of tasks created in [fromUtc, toUtc), for the CreatedVsCompleted dashboard widget
    /// (paired with the existing <see cref="CompletedCountAsync"/>).</summary>
    Task<int> CreatedCountAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>Task counts grouped by one custom field definition's stored value (Dropdown options
    /// resolved to their label; other simple types grouped by their raw stored value), for the
    /// CustomFieldBreakdown dashboard widget. Only tasks with a stored value for the field are counted —
    /// MultiSelect/Formula/Rollup/Relationship fields are not single-valued and are not supported.</summary>
    Task<IReadOnlyList<CustomFieldValueCount>> CustomFieldValueCountsAsync(Guid workspaceId, Guid definitionId, CancellationToken ct = default);
}
