namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;

public sealed class RecurringTaskService(
    WorkServiceContext ctx,
    ITaskListStore lists,
    IStatusSchemeStore schemes,
    IWorkItemStore tasks,
    IRecurringTaskStore recurring,
    IActivityStore activity,
    ITaskListMembershipStore memberships) : WorkServiceBase(ctx)
{
    public async Task<RecurringDto> CreateAsync(CreateRecurringCommand command, CancellationToken ct = default)
    {
        var list = await lists.FindAsync(command.ListId, ct) ?? throw new NotFoundException("List not found.");
        WorkManagementAuthorizer.EnsureManageStructure((await AccessAsync(list.WorkspaceId, ct))?.Role);

        ValidateTimeZone(command.TimeZoneId);

        var definition = RecurringTaskDefinition.Create(
            NewId(), list.WorkspaceId, list.Id, command.Title,
            command.Frequency, command.Interval, command.TimeZoneId, command.AnchorUtc, UserId);
        definition.SetDetails(command.Description, command.Priority ?? TaskPriority.None);

        recurring.Add(definition);
        Audit("recurring.created", nameof(RecurringTaskDefinition), definition.Id, new { command.Title, list.Id });
        await SaveAsync(ct);
        return WorkMapper.ToDto(definition);
    }

    /// <summary>
    /// Idempotently generates the occurrence due at or before <paramref name="asOfUtc"/> (ADR-0009).
    /// If the occurrence already exists (dedup key), returns without creating a duplicate.
    /// </summary>
    public async Task<GeneratedOccurrenceDto> RunAsync(Guid definitionId, DateTimeOffset? asOfUtc = null, CancellationToken ct = default)
    {
        var definition = await recurring.FindAsync(definitionId, ct)
            ?? throw new NotFoundException("Recurring definition not found.");
        WorkManagementAuthorizer.EnsureManageStructure((await AccessAsync(definition.WorkspaceId, ct))?.Role);

        return await GenerateAsync(definition, asOfUtc ?? Now, ct);
    }

    /// <summary>
    /// Core generation used by both the API and the background service. Bypasses per-request
    /// authorization (the caller — timer or authorized endpoint — has already been vetted).
    /// </summary>
    public async Task<GeneratedOccurrenceDto> GenerateAsync(RecurringTaskDefinition definition, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        if (!definition.IsActive || definition.NextRunUtc > asOfUtc)
        {
            return new GeneratedOccurrenceDto(definition.Id, false, null);
        }

        var occurrenceUtc = definition.NextRunUtc;
        var key = definition.OccurrenceKey(occurrenceUtc);

        // Idempotency guard: skip if this occurrence was already generated.
        if (await recurring.OccurrenceExistsAsync(definition.Id, key, ct))
        {
            definition.AdvanceAfter(occurrenceUtc, Now);
            await SaveAsync(ct);
            return new GeneratedOccurrenceDto(definition.Id, false, null);
        }

        var list = await lists.FindAsync(definition.ListId, ct)
            ?? throw new NotFoundException("The recurring definition's list no longer exists.");
        var scheme = await schemes.FindAsync(list.StatusSchemeId, ct)
            ?? throw new NotFoundException("Status scheme missing.");
        var status = scheme.DefaultStatus();

        var sequence = list.NextTaskSequence();
        var maxPos = await tasks.MaxPositionAsync(list.Id, ct);
        var position = Positioning.Append(maxPos);
        var actorUserId = definition.CreatedByUserId != Guid.Empty ? definition.CreatedByUserId : UserIdOrSystem();

        var task = WorkItem.Create(
            NewId(), list.WorkspaceId, list.SpaceId, list.Id, parentId: null,
            sequence, definition.Title, status.Id, status.IsCompletedCategory, position, actorUserId, Now);
        task.UpdateDetails(null, definition.Description, definition.Priority, null, occurrenceUtc, false, actorUserId, Now);

        tasks.Add(task);
        memberships.Add(new TaskListMembership(NewId(), list.WorkspaceId, task.Id, list.Id, isPrimary: true, position, Now));
        recurring.AddOccurrence(new RecurringOccurrence(NewId(), definition.Id, key, task.Id, Now));
        activity.Add(new TaskActivityEvent(NewId(), list.WorkspaceId, task.Id, actorUserId, "created_from_recurrence", null, Now));
        definition.AdvanceAfter(occurrenceUtc, Now);

        Audit("recurring.generated", nameof(RecurringTaskDefinition), definition.Id, new { taskId = task.Id, key });
        await SaveAsync(ct);
        return new GeneratedOccurrenceDto(definition.Id, true, task.Id);
    }

    private Guid UserIdOrSystem()
    {
        var user = Ctx.CurrentUser;
        return user.IsAuthenticated ? user.UserId : Guid.Empty;
    }

    private static void ValidateTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new ValidationAppException("A time zone is required for recurrence.");
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new ValidationAppException($"Unknown time zone '{timeZoneId}'.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new ValidationAppException($"Invalid time zone '{timeZoneId}'.");
        }
    }
}

public sealed class SavedViewService(WorkServiceContext ctx, ISavedViewStore views) : WorkServiceBase(ctx)
{
    public async Task<ViewDto> CreateAsync(CreateViewCommand command, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureEditContent((await AccessAsync(workspaceId, ct))?.Role);

        var view = SavedView.Create(
            NewId(), workspaceId, command.ScopeType, command.ScopeId,
            command.Name, command.ViewType, command.ConfigJson, command.IsPrivate, UserId, Now);
        views.Add(view);
        Audit("view.created", nameof(SavedView), view.Id, new { command.Name });
        await SaveAsync(ct);
        return WorkMapper.ToDto(view);
    }

    public async Task<IReadOnlyList<ViewDto>> ListAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);
        var list = await views.ListForUserAsync(workspaceId, UserId, ct);
        return list.Select(WorkMapper.ToDto).ToList();
    }

    public async Task<ViewDto> UpdateAsync(Guid viewId, string? name, string? configJson, bool? isPrivate, CancellationToken ct = default)
    {
        var view = await views.FindAsync(viewId, ct) ?? throw new NotFoundException("View not found.");
        WorkManagementAuthorizer.EnsureEditContent((await AccessAsync(view.WorkspaceId, ct))?.Role);
        if (view.IsPrivate && view.OwnerUserId != UserId)
        {
            throw new ForbiddenException("You can only modify your own private views.");
        }

        view.Update(name, configJson, isPrivate, Now);
        Audit("view.updated", nameof(SavedView), view.Id);
        await SaveAsync(ct);
        return WorkMapper.ToDto(view);
    }
}
