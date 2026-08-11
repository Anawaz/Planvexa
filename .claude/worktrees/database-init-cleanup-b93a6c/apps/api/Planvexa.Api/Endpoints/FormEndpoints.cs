namespace Planvexa.Api.Endpoints;

using Planvexa.Modules.Forms.Application;
using Planvexa.Modules.Forms.Application.Services;

// ---- Request models ----
public sealed record FormFieldRequest(
    string Label, string Type, bool Required, IReadOnlyList<string>? Options, int Position,
    Guid? ConditionFieldId = null, string? ConditionOperator = null, string? ConditionValue = null,
    Guid? CustomFieldDefinitionId = null);

public sealed record CreateFormRequest(Guid ListId, string Title, string? Description, IReadOnlyList<FormFieldRequest>? Fields);
public sealed record UpdateFormRequest(string? Title, string? Description, bool? IsActive, IReadOnlyList<FormFieldRequest>? Fields);

public sealed record UpdateFormSettingsRequest(
    string? BrandingLogoUrl, string? BrandingColor,
    string? ConfirmationMessage, string? ConfirmationRedirectUrl,
    int? MinSubmitSeconds, int? MaxTotalSubmissions, int? MaxSubmissionsPerRespondent,
    string? TargetStatusName, string? TargetPriority, string? TargetTagsCsv, Guid? TargetTeamId,
    int? DueDateDaysAfterSubmission);

/// <summary>Spam heuristic: <c>Honeypot</c> should always be empty (real users never see
/// that field) and <c>RenderedAtUtc</c> is when the client rendered the form, both purely additive to the
/// existing field-values shape.</summary>
public sealed record SubmitFormRequest(IReadOnlyDictionary<string, string> Values, string? Honeypot = null, DateTimeOffset? RenderedAtUtc = null);

/// <summary>Form authoring endpoints (extended with settings/export) + the anonymous
/// public submission surface (extended with file uploads + spam/rate/submission limits).
/// Submissions/exports stay behind the SAME Member+ authorization as authoring (FormsAuthorizer.EnsureEdit)
/// — only the field-definitions/submit endpoints under /public/forms are anonymous by design.</summary>
public static class FormEndpoints
{
    public static void MapFormEndpoints(this RouteGroupBuilder api)
    {
        MapAuthoring(api);
        MapPublic(api);
    }

    private static void MapAuthoring(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/forms").RequireAuthorization();

        group.MapGet("/", async (FormService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateFormRequest r, FormService svc, CancellationToken ct) =>
        {
            var fields = (r.Fields ?? Array.Empty<FormFieldRequest>()).Select(ToFieldInput).ToList();
            var dto = await svc.CreateAsync(new CreateFormCommand(r.ListId, r.Title, r.Description, fields), ct);
            return Results.Created($"/api/v1/forms/{dto.Id}", dto);
        });

        group.MapGet("/{id:guid}", async (Guid id, FormService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(id, ct)));

        group.MapPatch("/{id:guid}", async (Guid id, UpdateFormRequest r, FormService svc, CancellationToken ct) =>
        {
            var fields = r.Fields?.Select(ToFieldInput).ToList();
            return Results.Ok(await svc.UpdateAsync(id, new UpdateFormCommand(r.Title, r.Description, r.IsActive, fields), ct));
        });

        group.MapPatch("/{id:guid}/settings", async (Guid id, UpdateFormSettingsRequest r, FormService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateSettingsAsync(id, new UpdateFormSettingsCommand(
                r.BrandingLogoUrl, r.BrandingColor, r.ConfirmationMessage, r.ConfirmationRedirectUrl,
                r.MinSubmitSeconds, r.MaxTotalSubmissions, r.MaxSubmissionsPerRespondent,
                r.TargetStatusName, r.TargetPriority, r.TargetTagsCsv, r.TargetTeamId, r.DueDateDaysAfterSubmission), ct)));

        group.MapDelete("/{id:guid}", async (Guid id, FormService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/submissions", async (Guid id, FormService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListSubmissionsAsync(id, ct)));

        group.MapGet("/{id:guid}/submissions/export.csv", async (Guid id, FormService svc, CancellationToken ct) =>
        {
            var csv = await svc.ExportSubmissionsCsvAsync(id, ct);
            return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"form-{id}-submissions.csv");
        });

        group.MapGet("/{id:guid}/submissions/export.xlsx", async (Guid id, FormService svc, CancellationToken ct) =>
        {
            var xlsx = await svc.ExportSubmissionsXlsxAsync(id, ct);
            return Results.File(xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"form-{id}-submissions.xlsx");
        });
    }

    private static void MapPublic(RouteGroupBuilder api)
    {
        // Anonymous: the workspace is resolved from the public token, never from the body.
        var group = api.MapGroup("/public/forms").AllowAnonymous();

        group.MapGet("/{token}", async (string token, PublicFormService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(token, ct)));

        // Pre-submission upload for File Upload fields. Same rate-limit policy as
        // submission (a form-scoped upload flood is exactly as unwanted as a submission flood).
        group.MapPost("/{token}/uploads", async (string token, IFormFile file, PublicFormService svc, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                var dto = await svc.UploadFileAsync(token, file.FileName, file.ContentType, file.Length, content, ct);
                return Results.Ok(dto);
            })
            .RequireRateLimiting("form-submission")
            // Same reasoning as AttachmentEndpoints: bearer/token-authenticated surface (here, by public
            // form token), no cookie-driven session, so there is no cross-site request to forge.
            .DisableAntiforgery();

        group.MapPost("/{token}/submissions", async (string token, SubmitFormRequest r, HttpContext http, PublicFormService svc, CancellationToken ct) =>
            {
                var idempotencyKey = http.Request.Headers["Idempotency-Key"].ToString();
                var clientIp = http.Connection.RemoteIpAddress?.ToString();
                var result = await svc.SubmitAsync(token, r.Values, idempotencyKey, r.Honeypot, r.RenderedAtUtc, clientIp, ct);
                return Results.Ok(result);
            })
            .RequireRateLimiting("form-submission");
    }

    private static FormFieldInput ToFieldInput(FormFieldRequest f)
        => new(f.Label, f.Type, f.Required, f.Options, f.Position, f.ConditionFieldId, f.ConditionOperator, f.ConditionValue, f.CustomFieldDefinitionId);
}
