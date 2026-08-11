namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;

/// <summary>
/// Workspace-configurable task types, following the same "seed a built-in default
/// lazily on first read" pattern as WorkspaceProvisioningService does for the default StatusScheme.
/// </summary>
public sealed class TaskTypeService(WorkServiceContext ctx, ITaskTypeStore taskTypes) : WorkServiceBase(ctx)
{
    public async Task<IReadOnlyList<TaskTypeDto>> ListAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        if (await taskTypes.FindBuiltInAsync(workspaceId, ct) is null)
        {
            taskTypes.Add(TaskType.CreateBuiltIn(NewId(), workspaceId));
            await SaveAsync(ct);
        }

        var list = await taskTypes.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(WorkMapper.ToDto).ToList();
    }

    public async Task<TaskTypeDto> CreateAsync(CreateTaskTypeCommand command, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureManageStructure((await AccessAsync(workspaceId, ct))?.Role);

        var existing = await taskTypes.ListByWorkspaceAsync(workspaceId, ct);
        var position = existing.Count == 0 ? 0 : existing.Max(x => x.Position) + 1024;

        var type = TaskType.Create(NewId(), workspaceId, command.Name, command.Color, command.Icon, position);
        taskTypes.Add(type);
        Audit("task_type.created", nameof(TaskType), type.Id, new { command.Name });
        await SaveAsync(ct);
        return WorkMapper.ToDto(type);
    }

    public async Task<TaskTypeDto> UpdateAsync(Guid id, UpdateTaskTypeCommand command, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureManageStructure((await AccessAsync(workspaceId, ct))?.Role);

        var type = await taskTypes.FindAsync(id, ct);
        if (type is null || type.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Task type not found.");
        }

        type.Update(command.Name, command.Color, command.Icon);
        Audit("task_type.updated", nameof(TaskType), type.Id, new { command.Name });
        await SaveAsync(ct);
        return WorkMapper.ToDto(type);
    }
}
