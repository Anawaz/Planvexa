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

        var sprint = Sprint.Create(NewId(), workspaceId, command.Name, command.StartUtc, command.EndUtc, UserId, Now);
        sprints.Add(sprint);
        Audit("planning.sprint.created", "Sprint", sprint.Id, new { sprint.Name, sprint.StartDate, sprint.EndDate });
        await SaveAsync(ct);
        return ToDto(sprint);
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
        => new(s.Id, s.Name, new DateTimeOffset(s.StartDate, TimeSpan.Zero), new DateTimeOffset(s.EndDate, TimeSpan.Zero), s.Status.ToString(), s.TotalPoints());
}
