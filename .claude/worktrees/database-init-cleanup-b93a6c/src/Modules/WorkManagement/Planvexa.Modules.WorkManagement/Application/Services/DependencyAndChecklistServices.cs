namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;

public sealed class DependencyService(
    WorkServiceContext ctx, IWorkItemStore tasks, IDependencyStore dependencies) : WorkServiceBase(ctx)
{
    public async Task<DependencyDto> AddAsync(Guid taskId, AddDependencyCommand command, CancellationToken ct = default)
    {
        if (taskId == command.DependsOnTaskId)
        {
            throw new ValidationAppException("A task cannot depend on itself.");
        }

        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Task not found.");
        WorkManagementAuthorizer.EnsureEditContent((await AccessAsync(task.WorkspaceId, ct))?.Role);

        var other = await tasks.FindAsync(command.DependsOnTaskId, ct)
            ?? throw new NotFoundException("The depended-on task was not found.");
        if (other.WorkspaceId != task.WorkspaceId)
        {
            throw new ValidationAppException("Dependencies must be within the same workspace.");
        }

        var dependency = new TaskDependency(NewId(), taskId, command.DependsOnTaskId, command.Type);
        dependencies.Add(dependency);
        Activity(task.WorkspaceId, task.Id, "dependency_added", command.Type.ToString());
        Audit("task.dependency_added", "TaskDependency", dependency.Id, new { taskId, command.DependsOnTaskId, type = command.Type.ToString() });
        await SaveAsync(ct);
        return WorkMapper.ToDto(dependency);
    }

    public async Task RemoveAsync(Guid taskId, Guid dependencyId, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Task not found.");
        WorkManagementAuthorizer.EnsureEditContent((await AccessAsync(task.WorkspaceId, ct))?.Role);

        var dependency = await dependencies.FindAsync(dependencyId, ct);
        if (dependency is null || dependency.TaskId != taskId)
        {
            throw new NotFoundException("Dependency not found.");
        }

        dependencies.Remove(dependency);
        Audit("task.dependency_removed", "TaskDependency", dependency.Id);
        await SaveAsync(ct);
    }
}

public sealed class ChecklistService(
    WorkServiceContext ctx, IWorkItemStore tasks, IChecklistStore checklists) : WorkServiceBase(ctx)
{
    public async Task<ChecklistDto> AddChecklistAsync(Guid taskId, string name, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Task not found.");
        WorkManagementAuthorizer.EnsureEditContent((await AccessAsync(task.WorkspaceId, ct))?.Role);

        var checklist = TaskChecklist.Create(NewId(), taskId, name, Positioning.Step);
        checklists.Add(checklist);
        Audit("task.checklist_added", nameof(TaskChecklist), checklist.Id);
        await SaveAsync(ct);
        return WorkMapper.ToDto(checklist);
    }

    public async Task<ChecklistItemDto> AddItemAsync(Guid checklistId, string content, CancellationToken ct = default)
    {
        var checklist = await checklists.FindAsync(checklistId, ct) ?? throw new NotFoundException("Checklist not found.");
        var task = await tasks.FindAsync(checklist.TaskId, ct) ?? throw new NotFoundException("Task not found.");
        WorkManagementAuthorizer.EnsureEditContent((await AccessAsync(task.WorkspaceId, ct))?.Role);

        var max = await checklists.MaxItemPositionAsync(checklistId, ct);
        var item = checklist.AddItem(NewId(), content, Positioning.Append(max));
        await SaveAsync(ct);
        return new ChecklistItemDto(item.Id, item.Content, item.IsResolved, item.Position);
    }

    public async Task<ChecklistItemDto> UpdateItemAsync(Guid itemId, string? content, bool? isResolved, double? position, CancellationToken ct = default)
    {
        var item = await checklists.FindItemAsync(itemId, ct) ?? throw new NotFoundException("Checklist item not found.");
        var checklist = await checklists.FindAsync(item.ChecklistId, ct) ?? throw new NotFoundException("Checklist not found.");
        var task = await tasks.FindAsync(checklist.TaskId, ct) ?? throw new NotFoundException("Task not found.");
        WorkManagementAuthorizer.EnsureEditContent((await AccessAsync(task.WorkspaceId, ct))?.Role);

        item.Update(content, isResolved, position);
        await SaveAsync(ct);
        return new ChecklistItemDto(item.Id, item.Content, item.IsResolved, item.Position);
    }
}
