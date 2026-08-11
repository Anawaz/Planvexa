namespace Planvexa.Api.Endpoints;

using FluentValidation;
using Planvexa.Modules.Whiteboards.Application;
using Planvexa.Modules.Whiteboards.Application.Services;

// ---- Request models ----
public sealed record CreateWhiteboardRequest(string Name, bool IsPrivate, string? LinkedResourceType, Guid? LinkedResourceId, Guid? TemplateId);
public sealed record UpdateWhiteboardRequest(string? Name, bool? IsPrivate);
public sealed record CreateWhiteboardTemplateRequest(string Name);

public sealed class CreateWhiteboardRequestValidator : AbstractValidator<CreateWhiteboardRequest>
{
    public CreateWhiteboardRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

/// <summary>
/// Whiteboard endpoints. Content itself (shapes/connectors/sticky-notes/text/images)
/// is edited through the apps/collaboration Hocuspocus room, not this REST surface — these endpoints are
/// metadata CRUD, templates, and the collaboration-room authorization check the Node server relies on
/// (mirrors DocumentEndpoints exactly, see its class doc comment).
/// </summary>
public static class WhiteboardEndpoints
{
    public static void MapWhiteboardEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/whiteboards").RequireAuthorization();

        group.MapGet("/", async (WhiteboardService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateWhiteboardRequest r, WhiteboardService svc, CancellationToken ct) =>
        {
            var dto = await svc.CreateAsync(new CreateWhiteboardCommand(r.Name, r.IsPrivate, r.LinkedResourceType, r.LinkedResourceId, r.TemplateId), ct);
            return Results.Created($"/api/v1/whiteboards/{dto.Id}", dto);
        }).AddEndpointFilter<ValidationFilter<CreateWhiteboardRequest>>();

        group.MapGet("/{id:guid}", async (Guid id, WhiteboardService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(id, ct)));

        group.MapPatch("/{id:guid}", async (Guid id, UpdateWhiteboardRequest r, WhiteboardService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(id, new UpdateWhiteboardCommand(r.Name, r.IsPrivate), ct)));

        group.MapPost("/{id:guid}/archive", async (Guid id, WhiteboardService svc, CancellationToken ct) =>
        {
            await svc.ArchiveAsync(id, ct);
            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}", async (Guid id, WhiteboardService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        // Canvas image elements — no signed URLs, same bearer-authenticated
        // Content-Disposition: attachment download pattern as AttachmentEndpoints/ClipEndpoints.
        group.MapPost("/{id:guid}/images", async (Guid id, IFormFile file, WhiteboardService svc, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                var (imageId, contentType) = await svc.UploadImageAsync(id, file.ContentType, file.Length, content, ct);
                return Results.Created($"/api/v1/whiteboards/{id}/images/{imageId}", new { imageId, contentType });
            })
            .DisableAntiforgery();

        group.MapGet("/{id:guid}/images/{imageId:guid}", async (Guid id, Guid imageId, WhiteboardService svc, CancellationToken ct) =>
        {
            var content = await svc.DownloadImageAsync(id, imageId, ct);
            return Results.Stream(content, "application/octet-stream");
        });

        // Reusable content snapshots.
        var templateGroup = api.MapGroup("/whiteboard-templates").RequireAuthorization();

        templateGroup.MapGet("/", async (WhiteboardTemplateService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        templateGroup.MapPost("/from-whiteboard/{whiteboardId:guid}", async (Guid whiteboardId, CreateWhiteboardTemplateRequest r, WhiteboardTemplateService svc, CancellationToken ct) =>
        {
            var dto = await svc.CreateFromWhiteboardAsync(whiteboardId, r.Name, ct);
            return Results.Created($"/api/v1/whiteboard-templates/{dto.Id}", dto);
        });

        // The collaboration-room authorization check (CRITICAL — see class doc comment / WhiteboardService.CanCollaborateAsync).
        var internalGroup = api.MapGroup("/internal/whiteboards").RequireAuthorization();

        internalGroup.MapGet("/{id:guid}/can-collaborate", async (Guid id, WhiteboardService svc, CancellationToken ct) =>
            Results.Ok(await svc.CanCollaborateAsync(id, ct)));
    }
}
