namespace Planvexa.Modules.Goals.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Goals.Authorization;
using Planvexa.Modules.Goals.Domain;
using Planvexa.SharedContracts.Reporting;

/// <summary>
/// Goal CRUD + linked-task management + progress. SECURITY: a Goal's linked-tasks
/// completion RATIO is computed from the unfiltered task set (an aggregate percentage does not, by
/// itself, reveal any task's title/data), but the linked-tasks LIST returned for display
/// (<see cref="GetDetailAsync"/>) is always permission-filtered through
/// <see cref="IWorkReportingQueries.ReadableTaskCardsAsync"/> — the same pattern the Rollup fields
/// and the search already use — so a viewer can never see the title of a linked task they could not
/// otherwise read (a private task, or one in a private List/Space they have no grant on).
/// </summary>
public sealed class GoalService(GoalServiceContext ctx, IGoalStore goals, IGoalFolderStore folders, IWorkReportingQueries work)
    : GoalServiceBase(ctx)
{
    public async Task<IReadOnlyList<GoalDto>> ListAsync(Guid? folderId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GoalAuthorizer.EnsureRead(await RoleAsync(workspaceId, ct));

        var list = await goals.ListByWorkspaceAsync(workspaceId, folderId, ct);
        var dtos = new List<GoalDto>(list.Count);
        foreach (var goal in list)
        {
            dtos.Add(await ToDtoAsync(workspaceId, goal, ct));
        }

        return dtos;
    }

    public async Task<GoalDetailDto> GetDetailAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GoalAuthorizer.EnsureRead(await RoleAsync(workspaceId, ct));

        var goal = await goals.FindWithLinkedTasksAsync(workspaceId, id, ct) ?? throw new NotFoundException("Goal not found.");
        var dto = await ToDtoAsync(workspaceId, goal, ct);

        var taskIds = goal.LinkedTasks.Select(l => l.TaskId).ToList();
        var readable = taskIds.Count == 0
            ? new List<TaskCard>()
            : (await work.ReadableTaskCardsAsync(workspaceId, UserId, taskIds, ct)).ToList();
        var readableById = readable.ToDictionary(c => c.TaskId);

        var linkedDtos = goal.LinkedTasks
            .Select(l => readableById.TryGetValue(l.TaskId, out var card)
                ? new GoalLinkedTaskDto(l.TaskId, card.Title, card.IsCompleted, Visible: true)
                : new GoalLinkedTaskDto(l.TaskId, null, null, Visible: false))
            .ToList();

        return new GoalDetailDto(dto, linkedDtos);
    }

    public async Task<GoalDto> CreateAsync(CreateGoalCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GoalAuthorizer.EnsureEdit(await RoleAsync(workspaceId, ct));

        if (command.FolderId is { } fid && await folders.FindAsync(workspaceId, fid, ct) is null)
        {
            throw new NotFoundException("Goal folder not found.");
        }

        var goal = Goal.Create(
            NewId(), workspaceId, command.FolderId, command.Name, command.Description, command.OwnerUserId ?? UserId,
            command.StartDate, command.EndDate, command.TargetType, command.TargetValue, command.CurrentValue, Now);
        goals.Add(goal);
        Audit("goals.goal_created", "Goal", goal.Id, new { command.Name, command.TargetType });
        await SaveAsync(ct);
        return await ToDtoAsync(workspaceId, goal, ct);
    }

    public async Task<GoalDto> UpdateAsync(Guid id, UpdateGoalCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GoalAuthorizer.EnsureEdit(await RoleAsync(workspaceId, ct));

        var goal = await goals.FindAsync(workspaceId, id, ct) ?? throw new NotFoundException("Goal not found.");
        if (command.FolderId is { } fid && await folders.FindAsync(workspaceId, fid, ct) is null)
        {
            throw new NotFoundException("Goal folder not found.");
        }

        goal.Update(command.Name, command.Description, command.FolderId, command.StartDate, command.EndDate, command.CurrentValue, command.Status, Now);
        Audit("goals.goal_updated", "Goal", goal.Id);
        await SaveAsync(ct);
        return await ToDtoAsync(workspaceId, goal, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GoalAuthorizer.EnsureEdit(await RoleAsync(workspaceId, ct));

        var goal = await goals.FindAsync(workspaceId, id, ct) ?? throw new NotFoundException("Goal not found.");
        Audit("goals.goal_deleted", "Goal", goal.Id);
        goals.Remove(goal);
        await SaveAsync(ct);
    }

    public async Task<GoalDto> LinkTaskAsync(Guid id, Guid taskId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GoalAuthorizer.EnsureEdit(await RoleAsync(workspaceId, ct));

        var goal = await goals.FindWithLinkedTasksAsync(workspaceId, id, ct) ?? throw new NotFoundException("Goal not found.");
        goal.LinkTask(NewId(), taskId, Now);
        Audit("goals.task_linked", "Goal", goal.Id, new { taskId });
        await SaveAsync(ct);
        return await ToDtoAsync(workspaceId, goal, ct);
    }

    public async Task<GoalDto> UnlinkTaskAsync(Guid id, Guid taskId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GoalAuthorizer.EnsureEdit(await RoleAsync(workspaceId, ct));

        var goal = await goals.FindWithLinkedTasksAsync(workspaceId, id, ct) ?? throw new NotFoundException("Goal not found.");
        if (goal.UnlinkTask(taskId, Now))
        {
            Audit("goals.task_unlinked", "Goal", goal.Id, new { taskId });
            await SaveAsync(ct);
        }

        return await ToDtoAsync(workspaceId, goal, ct);
    }

    private async Task<GoalDto> ToDtoAsync(Guid workspaceId, Goal goal, CancellationToken ct)
    {
        var (total, completed) = goal.TargetType == GoalTargetType.LinkedTasksRatio
            ? await LinkedTaskCountsAsync(workspaceId, goal, ct)
            : (0, 0);

        var percent = GoalProgressCalculator.PercentComplete(goal, completed, total);
        return new GoalDto(
            goal.Id, goal.FolderId, goal.Name, goal.Description, goal.OwnerUserId, goal.StartDate, goal.EndDate,
            goal.TargetType, goal.TargetValue, goal.CurrentValue, goal.Status, percent, total, completed);
    }

    /// <summary>Unfiltered counts for the ratio itself (see class doc comment: the percentage alone does
    /// not leak task titles/data, only the detail view's task LIST needs permission filtering).</summary>
    private async Task<(int Total, int Completed)> LinkedTaskCountsAsync(Guid workspaceId, Goal goal, CancellationToken ct)
    {
        var linked = goal.LinkedTasks;
        if (linked.Count == 0)
        {
            return (0, 0);
        }

        var taskIds = linked.Select(l => l.TaskId).ToList();
        var cards = await work.TaskCardsAsync(workspaceId, taskIds, ct);
        return (taskIds.Count, cards.Count(c => c.IsCompleted));
    }
}
