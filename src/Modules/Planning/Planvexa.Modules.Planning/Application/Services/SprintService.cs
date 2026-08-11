namespace Planvexa.Modules.Planning.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Planning.Authorization;
using Planvexa.Modules.Planning.Domain;
using Planvexa.SharedContracts.Reporting;

/// <summary>Manages sprints and their items, and builds the sprint board grouped by task status.</summary>
public sealed class SprintService(
    PlanningServiceContext ctx,
    ISprintStore sprints,
    IWorkReportingQueries work)
    : PlanningServiceBase(ctx)
{
    public async Task<IReadOnlyList<SprintDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var list = await sprints.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<SprintDto> CreateAsync(CreateSprintCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var sprint = Sprint.Create(NewId(), workspaceId, command.Name, command.StartUtc, command.EndUtc, UserId, Now, command.Goal);
        sprints.Add(sprint);
        Audit("planning.sprint.created", "Sprint", sprint.Id, new { sprint.Name, sprint.StartDate, sprint.EndDate });
        await SaveAsync(ct);
        return ToDto(sprint);
    }

    public async Task<SprintDto> UpdateAsync(Guid sprintId, UpdateSprintCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var sprint = await sprints.FindAsync(sprintId, ct)
            ?? throw new NotFoundException("Sprint not found.");

        if (!string.IsNullOrWhiteSpace(command.Name))
        {
            sprint.Rename(command.Name);
        }

        if (command.StartUtc is not null || command.EndUtc is not null)
        {
            var start = command.StartUtc ?? new DateTimeOffset(sprint.StartDate, TimeSpan.Zero);
            var end = command.EndUtc ?? new DateTimeOffset(sprint.EndDate, TimeSpan.Zero);
            sprint.SetSchedule(start, end);
        }

        if (command.Goal is not null)
        {
            sprint.SetGoal(command.Goal);
        }

        Audit("planning.sprint.updated", "Sprint", sprint.Id, new { sprint.Name, sprint.StartDate, sprint.EndDate, sprint.Goal });
        await SaveAsync(ct);
        return ToDto(sprint);
    }

    public async Task DeleteAsync(Guid sprintId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var sprint = await sprints.FindAsync(sprintId, ct)
            ?? throw new NotFoundException("Sprint not found.");

        sprints.Remove(sprint);
        Audit("planning.sprint.deleted", "Sprint", sprintId);
        await SaveAsync(ct);
    }

    public async Task<SprintItemDto> AddItemAsync(Guid sprintId, AddSprintItemCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureEditContent((await AccessAsync(workspaceId, ct))?.Role);

        var sprint = await sprints.FindWithItemsAsync(sprintId, ct)
            ?? throw new NotFoundException("Sprint not found.");

        var item = sprint.AddItem(NewId(), command.TaskId, command.Points);
        Audit("planning.sprint.item.added", "SprintItem", item.Id, new { sprintId, command.TaskId, command.Points });
        await SaveAsync(ct);
        return new SprintItemDto(item.TaskId, item.Points);
    }

    public async Task RemoveItemAsync(Guid sprintId, Guid taskId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureEditContent((await AccessAsync(workspaceId, ct))?.Role);

        var sprint = await sprints.FindWithItemsAsync(sprintId, ct)
            ?? throw new NotFoundException("Sprint not found.");

        if (!sprint.RemoveItem(taskId))
        {
            throw new NotFoundException("Task is not in this sprint.");
        }

        Audit("planning.sprint.item.removed", "SprintItem", sprintId, new { taskId });
        await SaveAsync(ct);
    }

    public async Task<SprintDto> ChangeStatusAsync(Guid sprintId, ChangeSprintStatusCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureEditContent((await AccessAsync(workspaceId, ct))?.Role);

        var sprint = await sprints.FindAsync(sprintId, ct)
            ?? throw new NotFoundException("Sprint not found.");

        var fromStatus = sprint.Status;
        sprint.ChangeStatus(command.Status);
        Audit("planning.sprint.status_changed", "Sprint", sprint.Id, new { from = fromStatus.ToString(), to = command.Status.ToString() });
        await SaveAsync(ct);
        return ToDto(sprint);
    }

    /// <summary>Moves every item in <paramref name="sourceSprintId"/> whose task is not yet done into
    /// <paramref name="targetSprintId"/> (e.g. carrying unfinished work out of a completed sprint).
    /// "Done" reuses the same WorkItem.IsCompleted flag the sprint board and velocity widget already
    /// key off (via <see cref="TaskCard.IsCompleted"/>), not a hardcoded status id.</summary>
    public async Task CarryOverAsync(Guid sourceSprintId, Guid targetSprintId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureEditContent((await AccessAsync(workspaceId, ct))?.Role);

        var source = await sprints.FindWithItemsAsync(sourceSprintId, ct)
            ?? throw new NotFoundException("Sprint not found.");
        // RLS scopes FindWithItemsAsync to the ambient workspace, so a target sprint id from another
        // workspace resolves to null here -- same cross-workspace guard as Update/DeleteAsync above.
        var target = await sprints.FindWithItemsAsync(targetSprintId, ct)
            ?? throw new NotFoundException("Target sprint not found.");

        if (source.Items.Count == 0)
        {
            return;
        }

        var taskIds = source.Items.Select(i => i.TaskId).ToList();
        var cards = await work.TaskCardsAsync(workspaceId, taskIds, ct);
        var doneTaskIds = cards.Where(c => c.IsCompleted).Select(c => c.TaskId).ToHashSet();

        var carriedTaskIds = new List<Guid>();
        foreach (var item in source.Items.Where(i => !doneTaskIds.Contains(i.TaskId)).ToList())
        {
            target.AddItem(NewId(), item.TaskId, item.Points);
            source.RemoveItem(item.TaskId);
            carriedTaskIds.Add(item.TaskId);
        }

        if (carriedTaskIds.Count == 0)
        {
            return;
        }

        Audit("planning.sprint.carried_over", "Sprint", source.Id, new { sourceSprintId, targetSprintId, taskIds = carriedTaskIds });
        await SaveAsync(ct);
    }

    public async Task<SprintBoardDto> GetBoardAsync(Guid sprintId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var sprint = await sprints.FindWithItemsAsync(sprintId, ct)
            ?? throw new NotFoundException("Sprint not found.");

        var pointsByTask = sprint.Items.ToDictionary(i => i.TaskId, i => i.Points);
        var taskIds = sprint.Items.Select(i => i.TaskId).ToList();
        var cards = taskIds.Count == 0
            ? Array.Empty<TaskCard>()
            : (await work.TaskCardsAsync(workspaceId, taskIds, ct)).ToArray();

        var columns = cards
            .GroupBy(c => (c.StatusId, c.StatusName))
            .OrderBy(g => g.Key.StatusName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SprintBoardColumnDto(
                g.Key.StatusId,
                g.Key.StatusName,
                g.Select(c => new SprintBoardCardDto(c.TaskId, c.Title, pointsByTask.GetValueOrDefault(c.TaskId))).ToList()))
            .ToList();

        return new SprintBoardDto(sprint.Id, sprint.Name, columns);
    }

    private static SprintDto ToDto(Sprint s)
        => new(s.Id, s.Name, new DateTimeOffset(s.StartDate, TimeSpan.Zero), new DateTimeOffset(s.EndDate, TimeSpan.Zero), s.Status.ToString(), s.TotalPoints(), s.Goal);
}
