namespace Planvexa.Api.Endpoints;

using Planvexa.Modules.Documents.Application;
using Planvexa.Modules.Documents.Application.Services;
using Planvexa.SharedContracts.Workspaces;

// ---- Request models ----
public sealed record CreateDocumentRequest(string Title, string? Content, bool IsPrivate, Guid? SpaceId, Guid? ListId, Guid? TaskId, Guid? ParentDocumentId, Guid? TemplateId);
public sealed record UpdateDocumentRequest(string? Title, string? Content, bool? IsPrivate);
public sealed record SetDocumentParentRequest(Guid? ParentDocumentId);
public sealed record CreateDocumentTemplateRequest(string Name);
public sealed record AddDocumentCommentRequest(string Body);
public sealed record CreateDocumentShareRequest(int? ExpiresInDays, string? Password);

/// <summary>Collaborative document endpoints (extended with wiki hierarchy, templates,
/// Markdown export and the collaboration-room authorization check).</summary>
public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/documents").RequireAuthorization();

        group.MapGet("/", async (DocumentService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateDocumentRequest r, DocumentService svc, CancellationToken ct) =>
        {
            var dto = await svc.CreateAsync(new CreateDocumentCommand(r.Title, r.Content ?? string.Empty, r.IsPrivate, r.SpaceId, r.ListId, r.TaskId, r.ParentDocumentId, r.TemplateId), ct);
            return Results.Created($"/api/v1/documents/{dto.Id}", dto);
        });

        group.MapGet("/{id:guid}", async (Guid id, DocumentService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(id, ct)));

        group.MapPatch("/{id:guid}", async (Guid id, UpdateDocumentRequest r, DocumentService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(id, new UpdateDocumentCommand(r.Title, r.Content, r.IsPrivate), ct)));

        // Re-parent a document in the wiki tree (cycle-checked server-side).
        group.MapPost("/{id:guid}/parent", async (Guid id, SetDocumentParentRequest r, DocumentService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetParentAsync(id, r.ParentDocumentId, ct)));

        group.MapDelete("/{id:guid}", async (Guid id, DocumentService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/versions", async (Guid id, DocumentService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetVersionsAsync(id, ct)));

        group.MapPost("/{id:guid}/revert/{versionId:guid}", async (Guid id, Guid versionId, DocumentService svc, CancellationToken ct) =>
            Results.Ok(await svc.RevertAsync(id, versionId, ct)));

        // Markdown export.
        group.MapGet("/{id:guid}/export", async (Guid id, DocumentService svc, CancellationToken ct) =>
        {
            var markdown = await svc.ExportMarkdownAsync(id, ct);
            return Results.Text(markdown, "text/markdown");
        });

        // Images embedded in the rich-text content (see the editor's ImageNode). No signed URLs — same
        // bearer-authenticated streaming pattern as WhiteboardEndpoints' canvas images; served without
        // Content-Disposition so the browser renders it inline as an <img> source.
        group.MapPost("/{id:guid}/images", async (Guid id, IFormFile file, DocumentService svc, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                var (imageId, contentType) = await svc.UploadImageAsync(id, file.ContentType, file.Length, content, ct);
                return Results.Created($"/api/v1/documents/{id}/images/{imageId}", new { imageId, contentType });
            })
            .DisableAntiforgery();

        group.MapGet("/{id:guid}/images/{imageId:guid}", async (Guid id, Guid imageId, DocumentService svc, CancellationToken ct) =>
        {
            var content = await svc.DownloadImageAsync(id, imageId, ct);
            return Results.Stream(content, "application/octet-stream");
        });

        // File attachments embedded in the rich-text content (see the editor's FileAttachmentNode).
        // Unlike images, always served with Content-Disposition: attachment (fileDownloadName below) since
        // an arbitrary attached file isn't meant to render inline.
        group.MapPost("/{id:guid}/attachments", async (Guid id, IFormFile file, DocumentService svc, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                var (attachmentId, name, contentType, sizeBytes) = await svc.UploadAttachmentAsync(id, file.FileName, file.ContentType, file.Length, content, ct);
                return Results.Created(
                    $"/api/v1/documents/{id}/attachments/{attachmentId}/{Uri.EscapeDataString(name)}",
                    new { attachmentId, fileName = name, contentType, sizeBytes });
            })
            .DisableAntiforgery();

        group.MapGet("/{id:guid}/attachments/{attachmentId:guid}/{fileName}", async (Guid id, Guid attachmentId, string fileName, DocumentService svc, CancellationToken ct) =>
        {
            var content = await svc.DownloadAttachmentAsync(id, attachmentId, fileName, ct);
            return Results.Stream(content, "application/octet-stream", fileName);
        });

        // Comments — a flat, timestamped list gated by the same access rule as the document itself
        // (see DocumentCommentService's doc comment).
        group.MapGet("/{id:guid}/comments", async (Guid id, DocumentCommentService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(id, ct)));

        group.MapPost("/{id:guid}/comments", async (Guid id, AddDocumentCommentRequest r, DocumentCommentService svc, CancellationToken ct) =>
        {
            var dto = await svc.AddAsync(id, r.Body, ct);
            return Results.Created($"/api/v1/documents/{id}/comments/{dto.Id}", dto);
        });

        // Public, view-only share links — same expiration/revocation/password conventions as
        // CollaborationEndpoints' task share links (see DocumentShareLinkService's doc comment for why
        // this is a Documents-module duplicate rather than a cross-module reference).
        group.MapPost("/{id:guid}/share", async (Guid id, CreateDocumentShareRequest r, DocumentShareLinkService svc, CancellationToken ct) =>
            Results.Ok(await svc.CreateAsync(id, r.ExpiresInDays, r.Password, ct)));

        group.MapGet("/{id:guid}/shares", async (Guid id, DocumentShareLinkService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListForDocumentAsync(id, ct)));

        api.MapDelete("/document-shares/{id:guid}", async (Guid id, DocumentShareLinkService svc, CancellationToken ct) =>
        {
            await svc.RevokeAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization();

        // Private sharing (ADR-0003): grant/revoke/list explicit User/Team access to a document, so a
        // private document is not visible ONLY to its owner. Reuses GrantPermissionRequest and its
        // validator from ResourceSharingEndpoints (WorkManagement's equivalent) rather than redefining
        // an identical request shape.
        group.MapGet("/{id:guid}/permissions", async (Guid id, DocumentSharingService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(id, ct)));

        group.MapPost("/{id:guid}/permissions", async (Guid id, GrantPermissionRequest r, DocumentSharingService svc, CancellationToken ct) =>
            {
                var level = Enum.Parse<PermissionLevel>(r.Level, ignoreCase: true);
                var grant = await svc.GrantAsync(id, r.PrincipalType, r.PrincipalId, level, ct);
                return Results.Created($"/api/v1/documents/{id}/permissions", grant);
            })
            .AddEndpointFilter<ValidationFilter<GrantPermissionRequest>>();

        group.MapDelete("/{id:guid}/permissions/{principalType}/{principalId:guid}", async (
            Guid id, string principalType, Guid principalId, DocumentSharingService svc, CancellationToken ct) =>
        {
            await svc.RevokeAsync(id, principalType, principalId, ct);
            return Results.NoContent();
        });

        // Anonymous public read — exposes ONLY the shared document's rendered Markdown projection.
        // ?password= verifies a password-protected link; distinct 401 body shapes let the frontend
        // prompt vs. show "wrong", same convention as /public/tasks/{token}.
        api.MapGet("/public/documents/{token}", async (string token, string? password, DocumentShareLinkService svc, CancellationToken ct) =>
        {
            var result = await svc.GetSharedDocumentAsync(token, password, ct);
            return result.Status switch
            {
                DocumentShareAccessStatus.Ok => Results.Ok(result.Document),
                DocumentShareAccessStatus.PasswordRequired => Results.Json(new { requiresPassword = true }, statusCode: StatusCodes.Status401Unauthorized),
                DocumentShareAccessStatus.InvalidPassword => Results.Json(new { requiresPassword = true, invalid = true }, statusCode: StatusCodes.Status401Unauthorized),
                _ => Results.NotFound(),
            };
        }).AllowAnonymous();

        // Reusable content templates.
        var templateGroup = api.MapGroup("/document-templates").RequireAuthorization();

        templateGroup.MapGet("/", async (DocumentTemplateService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        templateGroup.MapPost("/from-document/{documentId:guid}", async (Guid documentId, CreateDocumentTemplateRequest r, DocumentTemplateService svc, CancellationToken ct) =>
        {
            var dto = await svc.CreateFromDocumentAsync(documentId, r.Name, ct);
            return Results.Created($"/api/v1/document-templates/{dto.Id}", dto);
        });

        // CRITICAL: the collaboration-room authorization check. The Node Hocuspocus server's
        // onAuthenticate hook calls this — forwarding the connecting user's own bearer token — before
        // admitting a WebSocket connection into a document's room. This is a normal authenticated endpoint
        // (same JwtBearer auth as every other /api/v1 route); the "internal" in the path is a convention
        // signalling service-to-service intent, not a separate trust boundary — the real gate is that it
        // re-runs DocumentService's own membership + Document.CanBeViewedBy checks for THIS document, every
        // call, rather than trusting the client's claim that it has access. See DocumentService.CanCollaborateAsync.
        var internalGroup = api.MapGroup("/internal/documents").RequireAuthorization();

        internalGroup.MapGet("/{id:guid}/can-collaborate", async (Guid id, DocumentService svc, CancellationToken ct) =>
            Results.Ok(await svc.CanCollaborateAsync(id, ct)));
    }
}
