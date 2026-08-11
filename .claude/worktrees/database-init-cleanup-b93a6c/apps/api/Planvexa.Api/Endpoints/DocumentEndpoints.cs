namespace Planvexa.Api.Endpoints;

using Planvexa.Modules.Documents.Application;
using Planvexa.Modules.Documents.Application.Services;

// ---- Request models ----
public sealed record CreateDocumentRequest(string Title, string? Content, bool IsPrivate, Guid? SpaceId, Guid? ListId, Guid? TaskId, Guid? ParentDocumentId, Guid? TemplateId);
public sealed record UpdateDocumentRequest(string? Title, string? Content, bool? IsPrivate);
public sealed record SetDocumentParentRequest(Guid? ParentDocumentId);
public sealed record CreateDocumentTemplateRequest(string Name);

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
