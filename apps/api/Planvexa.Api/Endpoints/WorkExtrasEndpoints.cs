namespace Planvexa.Api.Endpoints;

using Planvexa.Modules.WorkManagement.Application;
using Planvexa.Modules.WorkManagement.Application.Services;
using Planvexa.Modules.WorkManagement.Domain;

/// <summary>favourites — Space/Folder/List structural templates and per-user bookmarks.</summary>
public static class WorkExtrasEndpoints
{
    public static void MapWorkExtrasEndpoints(this RouteGroupBuilder api)
    {
        MapTemplates(api);
        MapFavorites(api);
        MapRecentItems(api);
    }

    private static void MapTemplates(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/templates").RequireAuthorization();

        group.MapGet("/", async (WorkTemplateService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateTemplateRequest r, WorkTemplateService svc, CancellationToken ct) =>
            {
                var command = new CreateTemplateCommand(
                    Enum.Parse<TemplateResourceType>(r.ResourceType, ignoreCase: true), r.SourceResourceId, r.Name);
                var dto = await svc.SaveAsTemplateAsync(command, ct);
                return Results.Created($"/api/v1/templates/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateTemplateRequest>>();

        group.MapPost("/{id:guid}/apply", async (Guid id, ApplyTemplateRequest r, WorkTemplateService svc, CancellationToken ct) =>
            {
                var command = new CreateFromTemplateCommand(id, r.SpaceId, r.FolderId, r.Name);
                return Results.Ok(await svc.CreateFromTemplateAsync(command, ct));
            })
            .AddEndpointFilter<ValidationFilter<ApplyTemplateRequest>>();
    }

    private static void MapFavorites(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/favorites").RequireAuthorization();

        group.MapGet("/", async (WorkFavoriteService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));

        // Toggle: returns { isFavorited } so the caller doesn't need to track prior state.
        group.MapPost("/toggle", async (ToggleFavoriteRequest r, WorkFavoriteService svc, CancellationToken ct) =>
            {
                var isFavorited = await svc.ToggleAsync(new ToggleFavoriteCommand(r.ResourceType, r.ResourceId), ct);
                return Results.Ok(new { isFavorited });
            })
            .AddEndpointFilter<ValidationFilter<ToggleFavoriteRequest>>();
    }

    private static void MapRecentItems(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/recent-items").RequireAuthorization();

        group.MapGet("/", async (int? limit, RecentItemService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(limit, ct)));

        group.MapPost("/", async (RecordRecentItemRequest r, RecentItemService svc, CancellationToken ct) =>
            {
                await svc.RecordViewAsync(new RecordRecentItemCommand(r.ResourceType, r.ResourceId), ct);
                return Results.NoContent();
            })
            .AddEndpointFilter<ValidationFilter<RecordRecentItemRequest>>();
    }
}
