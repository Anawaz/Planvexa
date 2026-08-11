namespace Planvexa.Api.Endpoints;

using Planvexa.Modules.WorkManagement.Application.Services;

/// <summary>
/// Task attachment upload/list/download/delete. Downloads stream straight from storage behind the
/// normal bearer auth (ADR-0006) — no signed URLs — and always carry
/// <c>Content-Disposition: attachment</c>, which is what neutralises stored XSS from HTML/SVG
/// uploads, so no MIME allowlist is needed.
/// </summary>
public static class AttachmentEndpoints
{
    public static void MapAttachmentEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/tasks/{id:guid}/attachments", async (
                Guid id, IFormFile file, AttachmentService svc, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                var dto = await svc.UploadAsync(id, file.FileName, file.ContentType, file.Length, content, ct);
                return Results.Created($"/api/v1/attachments/{dto.Id}", dto);
            })
            .RequireAuthorization()
            // Minimal-API form binding demands an antiforgery token; this API is bearer-authenticated
            // and registers no CORS policy, so there is no cookie-driven cross-site request to forge.
            .DisableAntiforgery();

        api.MapGet("/tasks/{id:guid}/attachments", async (Guid id, AttachmentService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(id, ct))).RequireAuthorization();

        api.MapGet("/attachments/{id:guid}/download", async (Guid id, AttachmentService svc, CancellationToken ct) =>
        {
            var (attachment, content) = await svc.DownloadAsync(id, ct);
            return Results.Stream(content, attachment.ContentType, attachment.FileName);
        }).RequireAuthorization();

        api.MapDelete("/attachments/{id:guid}", async (Guid id, AttachmentService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
