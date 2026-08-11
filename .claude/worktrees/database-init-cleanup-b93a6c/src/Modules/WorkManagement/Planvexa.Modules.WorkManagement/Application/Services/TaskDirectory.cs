namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.WorkManagement.Application;
using Planvexa.SharedContracts.Work;

/// <summary>Implements the cross-module <see cref="ITaskDirectory"/> over the task store.</summary>
public sealed class TaskDirectory(
    IWorkspaceContextAccessor workspaceAccessor,
    IWorkItemStore tasks,
    ITaskListStore lists) : ITaskDirectory
{
    public async Task<TaskRef?> FindAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var workspace = workspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            return null;
        }

        var task = await tasks.FindAsync(taskId, cancellationToken);
        if (task is null || task.IsDeleted)
        {
            return null;
        }

        var list = await lists.FindAsync(task.ListId, cancellationToken);
        return new TaskRef(
            task.Id,
            task.WorkspaceId,
            task.SpaceId,
            task.ListId,
            list?.Name ?? string.Empty,
            task.Title,
            task.IsCompleted);
    }

    public async Task<IReadOnlyList<DueTaskRef>> ListDueBetweenAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var due = await tasks.ListDueBetweenAsync(fromUtc, toUtc, cancellationToken);
        return due
            .Where(t => t.WorkspaceId == workspaceId)
            .Select(t => new DueTaskRef(t.Id, t.WorkspaceId, t.DueDate!.Value))
            .ToList();
    }

    public async Task<IReadOnlyList<TaskStatusAgeRef>> ListOpenTaskStatusAgesAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        // ListOpenTaskStatusAgesAsync is ambient-workspace-scoped (same as every other store query here);
        // workspaceId is accepted for interface symmetry with the other cross-module query contracts and
        // is not re-filtered on since the underlying WorkItem query is already workspace-isolated (RLS +
        // the global EF query filter — see PlanvexaDbContext).
        var ages = await tasks.ListOpenTaskStatusAgesAsync(cancellationToken);
        return ages.Select(a => new TaskStatusAgeRef(a.TaskId, a.StatusId, a.EnteredAtUtc)).ToList();
    }
}
