namespace Planvexa.Api.Endpoints;

using Planvexa.Modules.Clips.Application;
using Planvexa.Modules.Clips.Application.Services;

// ---- Request models ----
public sealed record UpdateClipRequest(string? Title, string? Description, bool? IsPrivate);
public sealed record AddClipCommentRequest(string Body);

/// <summary>
/// Clip endpoints: upload (both browser-recorded-then-uploaded and pre-recorded file
/// uploads go through the same multipart endpoint — see ClipService's class doc comment), download,
/// metadata, comments, and transcription. Downloads stream straight from storage behind the normal bearer
/// auth (ADR-0006, same "no signed URLs, Content-Disposition: attachment neutralises stored XSS" rule as
/// AttachmentEndpoints/ChatEndpoints' attachment download).
/// </summary>
public static class ClipEndpoints
{
    public static void MapClipEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/clips").RequireAuthorization();

        group.MapGet("/", async (ClipService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (
                string title, string? description, bool isPrivate, string? linkedResourceType, Guid? linkedResourceId, double? durationSeconds,
                IFormFile file, ClipService svc, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                var dto = await svc.UploadAsync(
                    title, description, isPrivate, linkedResourceType, linkedResourceId, durationSeconds,
                    file.FileName, file.ContentType, file.Length, content, ct);
                return Results.Created($"/api/v1/clips/{dto.Id}", dto);
            })
            // Minimal-API form binding demands an antiforgery token; this API is bearer-authenticated and
            // registers no CORS policy, so there is no cookie-driven cross-site request to forge (same
            // reasoning as AttachmentEndpoints/ImportEndpoints' upload).
            .DisableAntiforgery();

        group.MapGet("/{id:guid}", async (Guid id, ClipService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(id, ct)));

        group.MapGet("/{id:guid}/download", async (Guid id, ClipService svc, CancellationToken ct) =>
        {
            var (clip, content) = await svc.DownloadAsync(id, ct);
            return Results.Stream(content, clip.ContentType, enableRangeProcessing: true);
        });

        group.MapPatch("/{id:guid}", async (Guid id, UpdateClipRequest r, ClipService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(id, new UpdateClipCommand(r.Title, r.Description, r.IsPrivate), ct)));

        group.MapDelete("/{id:guid}", async (Guid id, ClipService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/comments", async (Guid id, ClipCommentService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(id, ct)));

        group.MapPost("/{id:guid}/comments", async (Guid id, AddClipCommentRequest r, ClipCommentService svc, CancellationToken ct) =>
        {
            var dto = await svc.AddAsync(id, r.Body, ct);
            return Results.Created($"/api/v1/clips/{id}/comments/{dto.Id}", dto);
        });

        group.MapGet("/{id:guid}/transcript", async (Guid id, ClipTranscriptService svc, CancellationToken ct) =>
        {
            var dto = await svc.GetAsync(id, ct);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        // Kicks off (or re-runs) transcription — see ClipTranscriptService/IClipTranscriber's doc comments
        // for what this does when no transcription-capable AI provider is configured (an honest
        // "Unavailable" result, never a faked transcript).
        group.MapPost("/{id:guid}/transcript", async (Guid id, ClipTranscriptService svc, CancellationToken ct) =>
            Results.Ok(await svc.RequestAsync(id, ct)));
    }
}
