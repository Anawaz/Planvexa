namespace Planvexa.Api.Endpoints;

using Planvexa.Api.Storage;
using Planvexa.BuildingBlocks.Abstractions;

/// <summary>
/// Local-disk-only signed URL endpoints. When <see cref="IFileStorage"/> resolves to
/// <see cref="LocalDiskFileStorage"/>, a "signed URL" is a Data-Protection-signed, time-limited token that
/// still proxies the bytes through this API — these two routes are that proxy. The S3-backed
/// implementation's signed URLs point directly at the object store and never hit these routes at all.
///
/// Anonymous by design: the minted token itself is the authorization. A token is only ever minted
/// server-side, after whichever module's normal authenticated + role-checked flow decided the caller may
/// read/write that storage path — the token is that decision, carried forward for a limited time, not a
/// bypass of it.
/// </summary>
public static class SignedFileEndpoints
{
    public static void MapSignedFileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/files/signed").AllowAnonymous();

        group.MapGet("/download", async (string token, IFileStorage storage, CancellationToken ct) =>
        {
            if (storage is not LocalDiskFileStorage local)
            {
                return Results.NotFound();
            }

            var validated = local.ValidateSignedToken(token, "download");
            if (validated is null)
            {
                return Results.Unauthorized();
            }

            var stream = await storage.OpenReadAsync(validated.Path, ct);
            return Results.File(stream, validated.ContentType ?? "application/octet-stream");
        });

        group.MapPut("/upload", async (string token, HttpRequest request, IFileStorage storage, IMalwareScanner scanner, CancellationToken ct) =>
        {
            if (storage is not LocalDiskFileStorage local)
            {
                return Results.NotFound();
            }

            var validated = local.ValidateSignedToken(token, "upload");
            if (validated is null)
            {
                return Results.Unauthorized();
            }

            // Same content-validation + malware-scan pipeline as every module's own upload endpoint.
            var content = await Planvexa.BuildingBlocks.Files.FileContentValidator.ValidateAsync(
                request.Body, fileName: validated.Path, contentType: validated.ContentType, ct);
            await scanner.EnsureCleanAsync(content, ct);
            await storage.SaveAsync(validated.Path, content, ct);
            return Results.NoContent();
        });
    }
}
