namespace Planvexa.Api.Endpoints;

using Planvexa.Api.Middleware;
using Planvexa.Modules.Integrations.Domain;
using Planvexa.Modules.WorkManagement.Application;
using Planvexa.Modules.WorkManagement.Application.Services;
using Planvexa.Modules.WorkManagement.Domain;

/// <summary>Task, dependency, checklist, custom-field-value, recurring and bulk endpoints.</summary>
public static class WorkTaskEndpoints
{
    public static void MapWorkTaskEndpoints(this RouteGroupBuilder api)
    {
        MapTasks(api);
        MapAssigneesWatchersTags(api);
        MapDependencies(api);
        MapChecklists(api);
        MapCustomFieldValues(api);
        MapReminders(api);
        MapRecurring(api);
        MapActivityFeed(api);
    }

    /// <summary>Workspace-wide, permission-filtered activity feed (distinct from the existing
    /// per-task GET /tasks/{id}/activity). Keyset-paginated: pass the last item's createdAtUtc back as
    /// `before` to fetch the next page.</summary>
    private static void MapActivityFeed(RouteGroupBuilder api)
    {
        api.MapGet("/activity", async (
                DateTimeOffset? before, int? take, Guid? actorUserId, DateTimeOffset? from, DateTimeOffset? to,
                WorkspaceActivityService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(before, take ?? 50, actorUserId, from, to, ct)))
            .RequireAuthorization();
    }

    private static TaskPriority? ParsePriority(string? value)
        => value is null ? null : Enum.Parse<TaskPriority>(value, ignoreCase: true);

    private static void MapTasks(RouteGroupBuilder api)
    {
        var tasks = api.MapGroup("/tasks").RequireAuthorization();

        tasks.MapPost("/", async (CreateTaskRequest r, HttpContext http, WorkItemService svc, CancellationToken ct) =>
            {
                var command = new CreateTaskCommand(
                    r.ListId, r.Title, r.Description, r.ParentId, ParsePriority(r.Priority),
                    r.StartDate, r.DueDate, r.IsMilestone, r.AssigneeUserIds, r.TagIds, r.StatusId,
                    r.TaskTypeId, r.CustomId);
                var dto = await svc.CreateAsync(command, IdempotencyKey(http), ct);
                return Results.Created($"/api/v1/tasks/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateTaskRequest>>()
            .RequireOAuthScope(OAuthScopes.TasksWrite);

        tasks.MapGet("/{id:guid}", async (Guid id, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(id, ct)))
            .RequireOAuthScope(OAuthScopes.TasksRead);

        // `?scope=created` returns tasks the caller created (My Work "Created by me" section),
        // `?scope=watching` returns tasks the caller watches (My Work "Watching" section), instead
        // of the default "assigned to me" list. `?workspaceId=` optionally scopes any of these down
        // to a single Workspace instead of spanning every Workspace the caller belongs to.
        tasks.MapGet("/mine", async (string? scope, Guid? workspaceId, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(scope switch
            {
                "created" => await svc.ListCreatedByMeAsync(workspaceId, ct),
                "watching" => await svc.ListWatchingAsync(workspaceId, ct),
                _ => await svc.ListMineAsync(workspaceId, ct),
            }));

        // My Work personal sort/organize preferences (product spec section 15) — global to the caller,
        // not scoped to any one Workspace (see MyWorkPreference's doc comment), so no X-Workspace header
        // is required for either of these two.
        tasks.MapGet("/mine/preferences", async (MyWorkPreferenceService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(ct)));

        tasks.MapPut("/mine/preferences", async (SaveMyWorkPreferencesRequest r, MyWorkPreferenceService svc, CancellationToken ct) =>
            Results.Ok(await svc.SaveAsync(new SaveMyWorkPreferenceCommand(r.SortBy, r.HiddenSections ?? []), ct)))
            .AddEndpointFilter<ValidationFilter<SaveMyWorkPreferencesRequest>>();

        tasks.MapPatch("/{id:guid}", async (Guid id, UpdateTaskRequest r, WorkItemService svc, CancellationToken ct) =>
        {
            var command = new UpdateTaskCommand(
                r.Title, r.Description, ParsePriority(r.Priority), r.StartDate, r.DueDate, r.IsMilestone, r.StatusId, r.Position,
                r.TaskTypeId, r.CustomId);
            return Results.Ok(await svc.UpdateAsync(id, command, ct));
        });

        tasks.MapPost("/{id:guid}/move", async (Guid id, MoveTaskRequest r, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.MoveAsync(id, new MoveTaskCommand(r.ListId, r.StatusId, r.Position), ct)));

        tasks.MapPost("/{id:guid}/complete", async (Guid id, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.CompleteAsync(id, ct)));

        tasks.MapPost("/{id:guid}/reopen", async (Guid id, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.ReopenAsync(id, ct)));

        tasks.MapPost("/{id:guid}/duplicate", async (Guid id, WorkItemService svc, CancellationToken ct) =>
        {
            var dto = await svc.DuplicateAsync(id, ct);
            return Results.Created($"/api/v1/tasks/{dto.Id}", dto);
        });

        // Cross-list copy — same as Duplicate, but the copy lands in a different List.
        tasks.MapPost("/{id:guid}/copy", async (Guid id, CopyTaskRequest r, WorkItemService svc, CancellationToken ct) =>
        {
            var dto = await svc.CopyToListAsync(id, r.TargetListId, ct);
            return Results.Created($"/api/v1/tasks/{dto.Id}", dto);
        });

        // Merge `id` (source) into r.TargetTaskId (target); the source is archived.
        tasks.MapPost("/{id:guid}/merge", async (Guid id, MergeTaskRequest r, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.MergeAsync(id, r.TargetTaskId, ct)));

        // Multi-list membership — list this task's lists / add to another / remove from one.
        tasks.MapGet("/{id:guid}/lists", async (Guid id, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListMembershipsAsync(id, ct)));

        tasks.MapPost("/{id:guid}/lists", async (Guid id, AddToListRequest r, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.AddToListAsync(id, r.ListId, ct)));

        tasks.MapDelete("/{id:guid}/lists/{listId:guid}", async (Guid id, Guid listId, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.RemoveFromListAsync(id, listId, ct)));

        // Generic "relates to" links.
        tasks.MapPost("/{id:guid}/relations", async (Guid id, RelationRequest r, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.AddRelationAsync(id, r.RelatedTaskId, ct)));

        tasks.MapDelete("/{id:guid}/relations/{relatedTaskId:guid}", async (Guid id, Guid relatedTaskId, WorkItemService svc, CancellationToken ct) =>
        {
            await svc.RemoveRelationAsync(id, relatedTaskId, ct);
            return Results.NoContent();
        });

        // Team assignees, alongside the existing individual /assignees below.
        tasks.MapPost("/{id:guid}/team-assignees", async (Guid id, TeamAssigneeRequest r, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.AddTeamAssigneeAsync(id, r.TeamId, ct)));

        tasks.MapDelete("/{id:guid}/team-assignees/{teamId:guid}", async (Guid id, Guid teamId, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.RemoveTeamAssigneeAsync(id, teamId, ct)));

        tasks.MapPost("/{id:guid}/archive", async (Guid id, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.ArchiveAsync(id, true, ct)));

        tasks.MapPost("/{id:guid}/unarchive", async (Guid id, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.ArchiveAsync(id, false, ct)));

        tasks.MapDelete("/{id:guid}", async (Guid id, WorkItemService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        // Irreversible — only a task already in the trash (soft-deleted) can be purged this way.
        tasks.MapDelete("/{id:guid}/permanent", async (Guid id, WorkItemService svc, CancellationToken ct) =>
        {
            await svc.PermanentDeleteAsync(id, ct);
            return Results.NoContent();
        });

        tasks.MapPost("/{id:guid}/restore", async (Guid id, WorkItemService svc, CancellationToken ct) =>
        {
            await svc.RestoreAsync(id, ct);
            return Results.NoContent();
        });

        tasks.MapPost("/bulk", async (BulkTaskRequest r, WorkItemService svc, CancellationToken ct) =>
        {
            var affected = await svc.BulkUpdateAsync(new BulkTaskUpdate(r.TaskIds, r.StatusId, r.AddAssigneeUserId, r.DueDate), ct);
            return Results.Ok(new { affected });
        });

        // Optional keyset pagination: `after` is the last task id from the previous page, `limit` bounds
        // the page size (mirrors GET /activity's before/take cursor). Neither param is required — an
        // absent cursor/limit returns every task in the list, unchanged from before pagination existed.
        api.MapGet("/lists/{listId:guid}/tasks", async (Guid listId, Guid? after, int? limit, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListByListAsync(listId, after, limit, ct)))
            .RequireAuthorization()
            .RequireOAuthScope(OAuthScopes.TasksRead);

        // Nested AND/OR filter groups. POST (not GET) because the filter tree doesn't fit
        // cleanly in a query string; a null/absent body returns the same result as the plain GET above.
        api.MapPost("/lists/{listId:guid}/tasks/query", async (Guid listId, FilterGroupDto? filter, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.QueryByListAsync(listId, filter, ct))).RequireAuthorization();

        // Tasks in this list with a non-empty value for a Location-type custom field.
        api.MapGet("/lists/{listId:guid}/custom-fields/{definitionId:guid}/locations", async (
                Guid listId, Guid definitionId, CustomFieldService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListLocationValuesAsync(listId, definitionId, ct)))
            .RequireAuthorization();

        // Snapshot the task's current Start/DueDate as its baseline.
        tasks.MapPost("/{id:guid}/baseline", async (Guid id, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetBaselineAsync(id, ct)));

        // Minimal email-to-task ingestion (see EmailIngestRequest's doc comment / MapEmailIngestion).
        api.MapPost("/lists/{listId:guid}/email-ingest", async (Guid listId, EmailIngestRequest r, WorkItemService svc, CancellationToken ct) =>
            {
                var command = new CreateTaskCommand(
                    listId, r.Subject, $"From: {r.From}\n\n{r.Body}", null, null, null, null, null, null, null, null);
                var dto = await svc.CreateAsync(command, ct: ct);
                return Results.Created($"/api/v1/tasks/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<EmailIngestRequest>>()
            .RequireAuthorization();

        api.MapGet("/tasks/{id:guid}/activity", async (Guid id, WorkItemService svc, CancellationToken ct) =>
            Results.Ok((await svc.GetAsync(id, ct)).Activity)).RequireAuthorization();

        MapTaskTypes(api);
    }

    private static void MapTaskTypes(RouteGroupBuilder api)
    {
        var types = api.MapGroup("/task-types").RequireAuthorization();

        types.MapGet("/", async (TaskTypeService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        types.MapPost("/", async (CreateTaskTypeRequest r, TaskTypeService svc, CancellationToken ct) =>
            {
                var dto = await svc.CreateAsync(new CreateTaskTypeCommand(r.Name, r.Color, r.Icon), ct);
                return Results.Created($"/api/v1/task-types/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateTaskTypeRequest>>();

        types.MapPatch("/{id:guid}", async (Guid id, UpdateTaskTypeRequest r, TaskTypeService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(id, new UpdateTaskTypeCommand(r.Name, r.Color, r.Icon), ct)));
    }

    private static void MapAssigneesWatchersTags(RouteGroupBuilder api)
    {
        var tasks = api.MapGroup("/tasks/{id:guid}").RequireAuthorization();

        tasks.MapPost("/assignees", async (Guid id, AssigneeRequest r, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.AddAssigneeAsync(id, r.UserId, ct)));

        tasks.MapDelete("/assignees/{userId:guid}", async (Guid id, Guid userId, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.RemoveAssigneeAsync(id, userId, ct)));

        tasks.MapPost("/watchers", async (Guid id, AssigneeRequest r, WorkItemService svc, CancellationToken ct) =>
        {
            await svc.AddWatcherAsync(id, r.UserId, ct);
            return Results.NoContent();
        });

        tasks.MapDelete("/watchers/{userId:guid}", async (Guid id, Guid userId, WorkItemService svc, CancellationToken ct) =>
        {
            await svc.RemoveWatcherAsync(id, userId, ct);
            return Results.NoContent();
        });

        tasks.MapPut("/tags", async (Guid id, SetTagsRequest r, WorkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetTagsAsync(id, r.TagIds, ct)));
    }

    private static void MapDependencies(RouteGroupBuilder api)
    {
        var tasks = api.MapGroup("/tasks/{id:guid}/dependencies").RequireAuthorization();

        tasks.MapPost("/", async (Guid id, AddDependencyRequest r, DependencyService svc, CancellationToken ct) =>
        {
            var type = Enum.Parse<DependencyType>(r.Type, ignoreCase: true);
            var dto = await svc.AddAsync(id, new AddDependencyCommand(r.DependsOnTaskId, type), ct);
            return Results.Created($"/api/v1/tasks/{id}/dependencies/{dto.Id}", dto);
        });

        tasks.MapDelete("/{depId:guid}", async (Guid id, Guid depId, DependencyService svc, CancellationToken ct) =>
        {
            await svc.RemoveAsync(id, depId, ct);
            return Results.NoContent();
        });
    }

    private static void MapChecklists(RouteGroupBuilder api)
    {
        api.MapPost("/tasks/{id:guid}/checklists", async (Guid id, CreateChecklistRequest r, ChecklistService svc, CancellationToken ct) =>
            Results.Ok(await svc.AddChecklistAsync(id, r.Name, ct))).RequireAuthorization();

        api.MapPost("/checklists/{id:guid}/items", async (Guid id, CreateChecklistItemRequest r, ChecklistService svc, CancellationToken ct) =>
            Results.Ok(await svc.AddItemAsync(id, r.Content, ct))).RequireAuthorization();

        api.MapPatch("/checklist-items/{id:guid}", async (Guid id, UpdateChecklistItemRequest r, ChecklistService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateItemAsync(id, r.Content, r.IsResolved, r.Position, ct))).RequireAuthorization();
    }

    private static void MapReminders(RouteGroupBuilder api)
    {
        api.MapPost("/tasks/{id:guid}/reminders", async (Guid id, CreateReminderRequest r, ReminderService svc, CancellationToken ct) =>
            {
                var dto = await svc.CreateAsync(new CreateReminderCommand(id, r.RemindAtUtc, r.Note), ct);
                return Results.Created($"/api/v1/reminders/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateReminderRequest>>()
            .RequireAuthorization();

        api.MapGet("/tasks/{id:guid}/reminders", async (Guid id, ReminderService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListForTaskAsync(id, ct))).RequireAuthorization();

        api.MapDelete("/reminders/{id:guid}", async (Guid id, ReminderService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static void MapCustomFieldValues(RouteGroupBuilder api)
    {
        api.MapPut("/tasks/{id:guid}/custom-fields/{definitionId:guid}",
            async (Guid id, Guid definitionId, SetCustomFieldRequest r, CustomFieldService svc, CancellationToken ct) =>
                Results.Ok(await svc.SetValueAsync(id, definitionId, r.Value, ct))).RequireAuthorization();

        // Relationship-type field — full-replacement set of linked task ids.
        api.MapPut("/tasks/{id:guid}/custom-fields/{definitionId:guid}/relationships",
            async (Guid id, Guid definitionId, SetCustomFieldRelationshipsRequest r, CustomFieldService svc, CancellationToken ct) =>
                Results.Ok(await svc.SetRelationshipValuesAsync(id, definitionId, new SetRelationshipValuesCommand(r.RelatedTaskIds), ct)))
            .RequireAuthorization();
    }

    private static void MapRecurring(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/recurring-tasks").RequireAuthorization();

        group.MapPost("/", async (CreateRecurringRequest r, RecurringTaskService svc, CancellationToken ct) =>
            {
                var command = new CreateRecurringCommand(
                    r.ListId, r.Title, r.Description, ParsePriority(r.Priority),
                    Enum.Parse<RecurrenceFrequency>(r.Frequency, ignoreCase: true), r.Interval, r.TimeZoneId, r.AnchorUtc);
                var dto = await svc.CreateAsync(command, ct);
                return Results.Created($"/api/v1/recurring-tasks/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateRecurringRequest>>();

        group.MapPost("/{id:guid}/run", async (Guid id, RecurringTaskService svc, CancellationToken ct) =>
            Results.Ok(await svc.RunAsync(id, null, ct)));
    }

    /// <summary>Offline-mutation-outbox replay guard (mirrors AiMobileEndpoints.IdempotencyKey): empty/whitespace reads as absent.</summary>
    private static string? IdempotencyKey(HttpContext http)
    {
        var key = http.Request.Headers["Idempotency-Key"].ToString();
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }
}
