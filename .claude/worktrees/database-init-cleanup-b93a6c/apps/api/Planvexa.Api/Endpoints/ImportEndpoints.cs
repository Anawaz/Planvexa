namespace Planvexa.Api.Endpoints;

using Planvexa.Modules.WorkManagement.Application.Services;

public sealed record SetImportMappingRequest(IReadOnlyDictionary<string, string> Mapping);

/// <summary>Bulk data importers: upload -> map columns (CSV/Excel) -> validate -> commit.</summary>
public static class ImportEndpoints
{
    public static void MapImportEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/imports").RequireAuthorization();

        group.MapGet("/sources", (ImportJobService svc) => Results.Ok(svc.SupportedSourceTypes));

        group.MapGet("/", async (ImportJobService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (
                string sourceType, IFormFile file, string? targetSpaceName, string? targetListName,
                ImportJobService svc, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                var dto = await svc.UploadAsync(sourceType, file.FileName, content, file.Length, targetSpaceName, targetListName, ct);
                return Results.Created($"/api/v1/imports/{dto.Id}", dto);
            })
            // Minimal-API form binding demands an antiforgery token; this API is bearer-authenticated and
            // registers no CORS policy, so there is no cookie-driven cross-site request to forge (same
            // reasoning as AttachmentEndpoints' upload).
            .DisableAntiforgery();

        group.MapGet("/{id:guid}", async (Guid id, ImportJobService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(id, ct)));

        group.MapGet("/{id:guid}/rows", async (Guid id, ImportJobService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListRowsAsync(id, ct)));

        group.MapPut("/{id:guid}/mapping", async (Guid id, SetImportMappingRequest r, ImportJobService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetMappingAsync(id, r.Mapping, ct)));

        group.MapPost("/{id:guid}/validate", async (Guid id, ImportJobService svc, CancellationToken ct) =>
            Results.Ok(await svc.ValidateAsync(id, ct)));

        // Resumable: calling this again after an interruption (or after fixing/re-mapping failed rows and
        // re-validating) only processes rows not already Committed — see ImportJobService.CommitAsync.
        group.MapPost("/{id:guid}/commit", async (Guid id, ImportJobService svc, CancellationToken ct) =>
            Results.Ok(await svc.CommitAsync(id, ct)));
    }
}
