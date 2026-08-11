namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Reporting;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Implements the cross-module <see cref="IWorkReportingQueries"/> over WorkManagement tables. Lives
/// in Infrastructure (which already owns the DbContext) and relies on the ambient workspace query
/// filter for isolation. the permission-aware methods (<see cref="ReadableTaskCardsAsync"/>) also
/// depend on <see cref="IResourcePermissionQuery"/>/<see cref="IResourceHierarchyQuery"/>/
/// <see cref="IWorkspaceAccessQuery"/> — Infrastructure already references every module (it owns the
/// shared DbContext), so applying WorkManagement's own <see cref="WorkManagementAuthorizer"/> here is the
/// same reuse CustomFieldService's Rollup evaluation does intra-module, just from the one place a
/// cross-module caller can reach it without breaking the modular-monolith boundary (AGENTS.md rule 7).
/// </summary>
internal sealed class WorkReportingQueries(
    PlanvexaDbContext db, IResourcePermissionQuery acl, IResourceHierarchyQuery hierarchy, IWorkspaceAccessQuery access)
    : IWorkReportingQueries
{
    public async Task<IReadOnlyList<StatusCount>> StatusCountsAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var rows = await (
            from t in db.Set<WorkItem>()
            join s in db.Set<StatusDefinition>() on t.StatusId equals s.Id
            where t.WorkspaceId == workspaceId && !t.IsDeleted
            group s by new { s.Id, s.Name, s.Category } into g
            select new { g.Key.Id, g.Key.Name, g.Key.Category, Count = g.Count() })
            .ToListAsync(ct);

        return rows
            .Select(r => new StatusCount(r.Id, r.Name, r.Category.ToString(), r.Count))
            .ToList();
    }

    public async Task<int> OverdueCountAsync(Guid workspaceId, DateTimeOffset nowUtc, CancellationToken ct = default)
        => await db.Set<WorkItem>()
            .CountAsync(t => t.WorkspaceId == workspaceId && !t.IsDeleted && !t.IsCompleted
                && t.DueDate != null && t.DueDate < nowUtc, ct);

    public async Task<int> CompletedCountAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
        => await db.Set<WorkItem>()
            .CountAsync(t => t.WorkspaceId == workspaceId && !t.IsDeleted && t.IsCompleted
                && t.CompletedAtUtc != null && t.CompletedAtUtc >= fromUtc && t.CompletedAtUtc < toUtc, ct);

    public async Task<IReadOnlyList<DatedTask>> DatedTasksAsync(
        Guid workspaceId, Guid? spaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var query = db.Set<WorkItem>().Include(t => t.Assignees)
            .Where(t => t.WorkspaceId == workspaceId && !t.IsDeleted
                && t.DueDate != null && t.DueDate >= fromUtc && t.DueDate < toUtc);
        if (spaceId is { } sid)
        {
            query = query.Where(t => t.SpaceId == sid);
        }

        var tasks = await query.OrderBy(t => t.DueDate).ToListAsync(ct);
        return await BuildDatedTasksAsync(tasks, ct);
    }

    public async Task<IReadOnlyList<DatedTask>> GanttTasksAsync(Guid workspaceId, Guid spaceId, CancellationToken ct = default)
    {
        var tasks = await db.Set<WorkItem>().Include(t => t.Assignees)
            .Where(t => t.WorkspaceId == workspaceId && t.SpaceId == spaceId && !t.IsDeleted)
            .OrderBy(t => t.StartDate ?? t.DueDate)
            .ToListAsync(ct);
        return await BuildDatedTasksAsync(tasks, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> AssignedTaskIdsByUserAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var rows = await (
            from a in db.Set<TaskAssignee>()
            join t in db.Set<WorkItem>() on a.TaskId equals t.Id
            where t.WorkspaceId == workspaceId && !t.IsDeleted && !t.IsCompleted
            select new { a.UserId, a.TaskId })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(x => x.TaskId).Distinct().ToList());
    }

    public async Task<IReadOnlyList<TaskCard>> TaskCardsAsync(Guid workspaceId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default)
    {
        if (taskIds.Count == 0)
        {
            return Array.Empty<TaskCard>();
        }

        var rows = await (
            from t in db.Set<WorkItem>()
            join s in db.Set<StatusDefinition>() on t.StatusId equals s.Id
            where t.WorkspaceId == workspaceId && !t.IsDeleted && taskIds.Contains(t.Id)
            select new { t.Id, t.Title, StatusId = s.Id, StatusName = s.Name, s.Category, t.IsCompleted })
            .ToListAsync(ct);

        return rows
            .Select(r => new TaskCard(r.Id, r.Title, r.StatusId, r.StatusName, r.Category.ToString(), r.IsCompleted))
            .ToList();
    }

    public async Task<IReadOnlyList<PortfolioSpaceRow>> PortfolioAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var rows = await (
            from t in db.Set<WorkItem>()
            join sp in db.Set<Space>() on t.SpaceId equals sp.Id
            where t.WorkspaceId == workspaceId && !t.IsDeleted
            group new { t.IsCompleted } by new { sp.Id, sp.Name } into g
            select new
            {
                g.Key.Id,
                g.Key.Name,
                Total = g.Count(),
                Completed = g.Count(x => x.IsCompleted),
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new PortfolioSpaceRow(r.Id, r.Name, r.Total, r.Completed))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> SpaceIdByTaskAsync(Guid workspaceId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default)
    {
        if (taskIds.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        var rows = await db.Set<WorkItem>()
            .Where(t => t.WorkspaceId == workspaceId && taskIds.Contains(t.Id))
            .Select(t => new { t.Id, t.SpaceId })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Id, r => r.SpaceId);
    }

    public async Task<IReadOnlyList<Guid>> OverdueTaskIdsAsync(Guid workspaceId, DateTimeOffset nowUtc, CancellationToken ct = default)
        => await db.Set<WorkItem>()
            .Where(t => t.WorkspaceId == workspaceId && !t.IsDeleted && !t.IsCompleted
                && t.DueDate != null && t.DueDate < nowUtc)
            .Select(t => t.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> CompletedTaskIdsAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
        => await db.Set<WorkItem>()
            .Where(t => t.WorkspaceId == workspaceId && !t.IsDeleted && t.IsCompleted
                && t.CompletedAtUtc != null && t.CompletedAtUtc >= fromUtc && t.CompletedAtUtc < toUtc)
            .Select(t => t.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> SpaceTaskIdsAsync(Guid workspaceId, Guid spaceId, bool? completedOnly, CancellationToken ct = default)
    {
        var query = db.Set<WorkItem>().Where(t => t.WorkspaceId == workspaceId && t.SpaceId == spaceId && !t.IsDeleted);
        if (completedOnly is { } filter)
        {
            query = query.Where(t => t.IsCompleted == filter);
        }

        return await query.Select(t => t.Id).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TaskCard>> ReadableTaskCardsAsync(Guid workspaceId, Guid userId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default)
    {
        if (taskIds.Count == 0)
        {
            return Array.Empty<TaskCard>();
        }

        var tasks = await db.Set<WorkItem>()
            .Include(t => t.Assignees)
            .Where(t => t.WorkspaceId == workspaceId && !t.IsDeleted && taskIds.Contains(t.Id))
            .ToListAsync(ct);
        if (tasks.Count == 0)
        {
            return Array.Empty<TaskCard>();
        }

        var statusIds = tasks.Select(t => t.StatusId).Distinct().ToList();
        var statuses = await db.Set<StatusDefinition>()
            .Where(s => statusIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var role = (await access.GetAccessAsync(workspaceId, userId, ct))?.Role;
        var cards = new List<TaskCard>(tasks.Count);
        foreach (var task in tasks)
        {
            if (!await WorkManagementAuthorizer.CanReadAsync(task, role, userId, WorkResourceTypes.Task, acl, hierarchy, ct))
            {
                continue;
            }

            var status = statuses.GetValueOrDefault(task.StatusId);
            cards.Add(new TaskCard(task.Id, task.Title, task.StatusId, status?.Name ?? string.Empty, status?.Category.ToString() ?? string.Empty, task.IsCompleted));
        }

        return cards;
    }

    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset?>> CompletedAtByTaskIdsAsync(Guid workspaceId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default)
    {
        if (taskIds.Count == 0)
        {
            return new Dictionary<Guid, DateTimeOffset?>();
        }

        var rows = await db.Set<WorkItem>()
            .Where(t => t.WorkspaceId == workspaceId && taskIds.Contains(t.Id))
            .Select(t => new { t.Id, t.CompletedAtUtc })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Id, r => r.CompletedAtUtc);
    }

    public async Task<IReadOnlyList<MilestoneRow>> MilestonesAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var rows = await db.Set<WorkItem>()
            .Where(t => t.WorkspaceId == workspaceId && !t.IsDeleted && t.IsMilestone)
            .Select(t => new { t.Id, t.SpaceId, t.Title, t.DueDate, t.IsCompleted })
            .ToListAsync(ct);

        return rows.Select(r => new MilestoneRow(r.Id, r.SpaceId, r.Title, r.DueDate, r.IsCompleted)).ToList();
    }

    private async Task<IReadOnlyList<DatedTask>> BuildDatedTasksAsync(List<WorkItem> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0)
        {
            return Array.Empty<DatedTask>();
        }

        var ids = tasks.Select(t => t.Id).ToList();

        // A task "depends on" its predecessors: those it is BlockedBy, and those that Block it.
        var deps = await db.Set<TaskDependency>()
            .Where(d => ids.Contains(d.TaskId) || ids.Contains(d.DependsOnTaskId))
            .ToListAsync(ct);

        var dependsOn = new Dictionary<Guid, List<Guid>>();
        foreach (var d in deps)
        {
            if (d.Type == DependencyType.BlockedBy && ids.Contains(d.TaskId))
            {
                (dependsOn.TryGetValue(d.TaskId, out var l) ? l : dependsOn[d.TaskId] = new List<Guid>()).Add(d.DependsOnTaskId);
            }
            else if (d.Type == DependencyType.Blocks && ids.Contains(d.DependsOnTaskId))
            {
                (dependsOn.TryGetValue(d.DependsOnTaskId, out var l) ? l : dependsOn[d.DependsOnTaskId] = new List<Guid>()).Add(d.TaskId);
            }
        }

        return tasks
            .Select(t => new DatedTask(
                t.Id, t.ListId, t.SpaceId, t.Title, t.StartDate, t.DueDate, t.IsMilestone, t.IsCompleted,
                t.Priority.ToString(),
                t.Assignees.Select(a => a.UserId).ToList(),
                dependsOn.TryGetValue(t.Id, out var dep) ? dep : new List<Guid>(),
                t.BaselineStartDate, t.BaselineDueDate))
            .ToList();
    }
}
