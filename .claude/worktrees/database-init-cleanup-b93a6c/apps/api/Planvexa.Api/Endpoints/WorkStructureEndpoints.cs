namespace Planvexa.Api.Endpoints;

using Planvexa.Modules.WorkManagement.Application;
using Planvexa.Modules.WorkManagement.Application.Services;
using Planvexa.Modules.WorkManagement.Domain;

/// <summary>Hierarchy, status, tag, custom-field and view endpoints.</summary>
public static class WorkStructureEndpoints
{
    public static void MapWorkStructureEndpoints(this RouteGroupBuilder api)
    {
        MapSpaces(api);
        MapFolders(api);
        MapLists(api);
        MapStatusSchemes(api);
        MapTags(api);
        MapCustomFields(api);
        MapViews(api);
    }

    private static void MapSpaces(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/spaces").RequireAuthorization();

        group.MapPost("/", async (CreateSpaceRequest r, SpaceService svc, CancellationToken ct) =>
            {
                var dto = await svc.CreateAsync(new CreateSpaceCommand(r.Name, r.Description, r.Color, r.Icon), ct);
                return Results.Created($"/api/v1/spaces/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateSpaceRequest>>();

        group.MapGet("/", async (SpaceService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));

        group.MapPatch("/{id:guid}", async (Guid id, UpdateSpaceRequest r, SpaceService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(id, new UpdateSpaceCommand(r.Name, r.Description, r.Color, r.Icon, r.Position), ct)));

        group.MapPost("/{id:guid}/archive", async (Guid id, SpaceService svc, CancellationToken ct) =>
        {
            await svc.ArchiveAsync(id, true, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/restore", async (Guid id, SpaceService svc, CancellationToken ct) =>
        {
            await svc.RestoreAsync(id, ct);
            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}", async (Guid id, SpaceService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPut("/{id:guid}/default-view", async (Guid id, SetDefaultViewRequest r, SpaceService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetDefaultViewAsync(id, r.ViewId, ct)));
    }

    private static void MapFolders(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/spaces/{spaceId:guid}/folders").RequireAuthorization();

        group.MapPost("/", async (Guid spaceId, CreateFolderRequest r, FolderService svc, CancellationToken ct) =>
        {
            var dto = await svc.CreateAsync(new CreateFolderCommand(spaceId, r.ParentFolderId, r.Name), ct);
            return Results.Created($"/api/v1/folders/{dto.Id}", dto);
        });

        group.MapGet("/", async (Guid spaceId, FolderService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(spaceId, ct)));

        var items = api.MapGroup("/folders").RequireAuthorization();

        items.MapGet("/{id:guid}", async (Guid id, FolderService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(id, ct)));

        items.MapPatch("/{id:guid}", async (Guid id, UpdateFolderRequest r, FolderService svc, CancellationToken ct) =>
            Results.Ok(await svc.RenameAsync(id, r.Name, ct)))
            .AddEndpointFilter<ValidationFilter<UpdateFolderRequest>>();

        items.MapDelete("/{id:guid}", async (Guid id, FolderService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        // Arbitrary-depth re-parenting with cycle prevention (FolderService.MoveAsync).
        items.MapPost("/{id:guid}/move", async (Guid id, MoveFolderRequest r, FolderService svc, CancellationToken ct) =>
            Results.Ok(await svc.MoveAsync(id, r.ParentFolderId, ct)));

        // Deep-copies the folder (subfolders to any depth, their lists and tasks).
        items.MapPost("/{id:guid}/duplicate", async (Guid id, FolderService svc, CancellationToken ct) =>
        {
            var dto = await svc.DuplicateAsync(id, ct);
            return Results.Created($"/api/v1/folders/{dto.Id}", dto);
        });

        items.MapPut("/{id:guid}/default-view", async (Guid id, SetDefaultViewRequest r, FolderService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetDefaultViewAsync(id, r.ViewId, ct)));
    }

    private static void MapLists(RouteGroupBuilder api)
    {
        var lists = api.MapGroup("/lists").RequireAuthorization();

        lists.MapPost("/", async (CreateListRequest r, TaskListService svc, CancellationToken ct) =>
            {
                var dto = await svc.CreateAsync(new CreateListCommand(r.SpaceId, r.FolderId, r.Name, r.Description, r.StatusSchemeId), ct);
                return Results.Created($"/api/v1/lists/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateListRequest>>();

        lists.MapPatch("/{id:guid}", async (Guid id, UpdateListRequest r, TaskListService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(id, new UpdateListCommand(r.Name, r.Description), ct)));

        lists.MapGet("/{id:guid}", async (Guid id, TaskListService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(id, ct)));

        lists.MapPost("/{id:guid}/archive", async (Guid id, TaskListService svc, CancellationToken ct) =>
        {
            await svc.ArchiveAsync(id, true, ct);
            return Results.NoContent();
        });

        lists.MapPost("/{id:guid}/restore", async (Guid id, TaskListService svc, CancellationToken ct) =>
        {
            await svc.RestoreAsync(id, ct);
            return Results.NoContent();
        });

        lists.MapDelete("/{id:guid}", async (Guid id, TaskListService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        // Copies the list's tasks (fields, assignees, watchers, tags, checklists, custom fields).
        lists.MapPost("/{id:guid}/duplicate", async (Guid id, TaskListService svc, CancellationToken ct) =>
        {
            var dto = await svc.DuplicateAsync(id, ct);
            return Results.Created($"/api/v1/lists/{dto.Id}", dto);
        });

        lists.MapPut("/{id:guid}/default-view", async (Guid id, SetDefaultViewRequest r, TaskListService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetDefaultViewAsync(id, r.ViewId, ct)));

        // This list's own + Space/Workspace + ancestor-Folder-inherited custom fields.
        lists.MapGet("/{id:guid}/custom-fields", async (Guid id, CustomFieldService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListEffectiveForListAsync(id, ct)));

        api.MapGet("/spaces/{spaceId:guid}/lists", async (Guid spaceId, TaskListService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListBySpaceAsync(spaceId, ct))).RequireAuthorization();
    }

    private static void MapStatusSchemes(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/status-schemes").RequireAuthorization();

        group.MapGet("/", async (StatusSchemeService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateStatusSchemeRequest r, StatusSchemeService svc, CancellationToken ct) =>
        {
            var statuses = r.Statuses
                .Select(s => (s.Name, Enum.Parse<StatusCategory>(s.Category, ignoreCase: true), s.Color))
                .ToList();
            var dto = await svc.CreateAsync(r.Name, statuses, ct);
            return Results.Created($"/api/v1/status-schemes/{dto.Id}", dto);
        });
    }

    private static void MapTags(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/tags").RequireAuthorization();
        group.MapGet("/", async (TagService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));
        group.MapPost("/", async (CreateTagRequest r, TagService svc, CancellationToken ct) =>
            Results.Created("/api/v1/tags", await svc.CreateAsync(r.Name, r.Color, ct)));
    }

    private static void MapCustomFields(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/custom-fields").RequireAuthorization();

        group.MapGet("/", async (CustomFieldService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateCustomFieldRequest r, CustomFieldService svc, CancellationToken ct) =>
            {
                var command = new CreateCustomFieldCommand(
                    r.Name,
                    Enum.Parse<CustomFieldType>(r.Type, ignoreCase: true),
                    Enum.Parse<CustomFieldScope>(r.Scope, ignoreCase: true),
                    r.ScopeId, r.IsRequired,
                    r.Options?.Select(o => new CustomFieldOptionInput(o.Label, o.Color)).ToList(),
                    r.FormulaExpression,
                    r.RollupSourceType is null ? null : Enum.Parse<CustomFieldRollupSourceType>(r.RollupSourceType, ignoreCase: true),
                    r.RollupSourceFieldId, r.RollupTargetFieldId,
                    r.RollupFunction is null ? null : Enum.Parse<CustomFieldRollupFunction>(r.RollupFunction, ignoreCase: true));
                var dto = await svc.CreateAsync(command, ct);
                return Results.Created($"/api/v1/custom-fields/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateCustomFieldRequest>>();
    }

    private static void MapViews(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/views").RequireAuthorization();

        group.MapGet("/", async (SavedViewService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateViewRequest r, SavedViewService svc, CancellationToken ct) =>
        {
            var command = new CreateViewCommand(
                Enum.Parse<SavedViewType>(r.ViewType, ignoreCase: true),
                Enum.Parse<CustomFieldScope>(r.ScopeType, ignoreCase: true),
                r.ScopeId, r.Name, string.IsNullOrWhiteSpace(r.Config) ? "{}" : r.Config!, r.IsPrivate);
            var dto = await svc.CreateAsync(command, ct);
            return Results.Created($"/api/v1/views/{dto.Id}", dto);
        });

        group.MapPatch("/{id:guid}", async (Guid id, UpdateViewRequest r, SavedViewService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(id, r.Name, r.Config, r.IsPrivate, ct)));
    }
}
