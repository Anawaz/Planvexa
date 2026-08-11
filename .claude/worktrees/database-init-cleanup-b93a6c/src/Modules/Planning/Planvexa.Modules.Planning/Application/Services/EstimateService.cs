namespace Planvexa.Modules.Planning.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Planning.Authorization;
using Planvexa.Modules.Planning.Domain;
using Planvexa.SharedContracts.Work;

/// <summary>Manages per-task effort estimates (planning-owned).</summary>
public sealed class EstimateService(
    PlanningServiceContext ctx,
    IEstimateStore estimates,
    ITaskDirectory tasks)
    : PlanningServiceBase(ctx)
{
    public async Task<EstimateDto?> GetAsync(Guid taskId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var estimate = await estimates.FindAsync(workspaceId, taskId, ct);
        return estimate is null ? null : new EstimateDto(estimate.TaskId, estimate.EstimateSeconds);
    }

    public async Task<EstimateDto> SetAsync(Guid taskId, long estimateSeconds, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureEditContent((await AccessAsync(workspaceId, ct))?.Role);

        // Validate the task exists in the current workspace via the cross-module directory.
        var task = await tasks.FindAsync(taskId, ct)
            ?? throw new NotFoundException("Task not found.");
        if (task.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Task not found in this workspace.");
        }

        var estimate = await estimates.FindAsync(workspaceId, taskId, ct);
        if (estimate is null)
        {
            estimate = TaskEstimate.Create(NewId(), workspaceId, taskId, estimateSeconds, Now);
            estimates.Add(estimate);
        }
        else
        {
            estimate.Set(estimateSeconds, Now);
        }

        Audit("planning.estimate.set", "TaskEstimate", estimate.Id, new { taskId, estimateSeconds });
        await SaveAsync(ct);
        return new EstimateDto(estimate.TaskId, estimate.EstimateSeconds);
    }
}
