namespace Planvexa.Api.Endpoints;

using Planvexa.Api.Ai;
using Planvexa.Api.Search;
using Planvexa.Modules.Ai.Application;
using Planvexa.Modules.Ai.Application.Services;
using Planvexa.Modules.Governance.Application;
using Planvexa.Modules.Governance.Application.Services;
using Planvexa.Modules.Mobile.Application;
using Planvexa.Modules.Mobile.Application.Services;
using Planvexa.Modules.WorkManagement.Application.Services;

// ---- Request models ----
// Gap-closer: Endpoint/P256dh/Auth are the browser PushSubscription's own fields
// (subscription.endpoint, keys.p256dh, keys.auth). Optional/nullable so older clients (and non-Web
// platforms) that don't send them keep working unchanged -- see DeviceRegistration's doc comment.
public sealed record RegisterDeviceRequest(
    string Platform, string PushToken, string? AppVersion,
    string? Endpoint = null, string? P256dh = null, string? Auth = null);
public sealed record UpdateRetentionPolicyRequest(int? DeletedTaskRetentionDays, int? AuditRetentionDays, bool? LegalHold);

/// <summary>A null/blank <c>ApiKey</c> keeps the stored key (write-only secret).</summary>
public sealed record UpdateAiProviderSettingsRequest(string? BaseUrl, string? Model, string? ApiKey, bool IsEnabled);

/// <summary>item 2+3: model allow-list + redaction configuration.</summary>
public sealed record UpdateAiGovernanceRequest(
    IReadOnlyList<string>? AllowedModels, bool RedactEmails, bool RedactApiKeys, bool RedactCreditCards,
    IReadOnlyList<string>? CustomRedactionPatterns);

public sealed record AskWorkspaceRequest(string Question);

/// <summary>AI assistance, mobile (devices + sync) and data-retention endpoints.</summary>
public static class AiMobileEndpoints
{
    public static void MapAiMobileEndpoints(this RouteGroupBuilder api)
    {
        MapAi(api);
        MapMobile(api);
        MapRetention(api);
    }

    private static void MapAi(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/ai").RequireAuthorization();

        group.MapPost("/tasks/{taskId:guid}/summarize", async (Guid taskId, HttpContext http, AiAssistService svc, CancellationToken ct) =>
            Results.Ok(await svc.SummarizeAsync(taskId, IdempotencyKey(http), ct)));

        group.MapPost("/tasks/{taskId:guid}/subtasks", async (Guid taskId, HttpContext http, AiAssistService svc, CancellationToken ct) =>
            Results.Ok(await svc.SuggestSubtasksAsync(taskId, IdempotencyKey(http), ct)));

        group.MapPost("/tasks/{taskId:guid}/priority", async (Guid taskId, HttpContext http, AiAssistService svc, CancellationToken ct) =>
            Results.Ok(await svc.SuggestPriorityAsync(taskId, IdempotencyKey(http), ct)));

        group.MapGet("/usage", async (AiAssistService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetUsageAsync(ct)));

        // Remaining capabilities that reuse AiAssistService's existing task-content permission
        // pattern (comments) or their own module's permission check (document/chat), documented on each
        // service method.
        group.MapPost("/tasks/{taskId:guid}/comments/summarize", async (Guid taskId, HttpContext http, AiAssistService svc, CancellationToken ct) =>
            Results.Ok(await svc.SummarizeCommentsAsync(taskId, IdempotencyKey(http), ct)));

        group.MapPost("/tasks/{taskId:guid}/risk", async (Guid taskId, HttpContext http, AiAssistService svc, CancellationToken ct) =>
            Results.Ok(await svc.DetectRiskAsync(taskId, IdempotencyKey(http), ct)));

        group.MapPost("/tasks/{taskId:guid}/duplicates", async (Guid taskId, DuplicateTaskService svc, CancellationToken ct) =>
            Results.Ok(await svc.FindDuplicatesAsync(taskId, ct)));

        group.MapPost("/documents/{documentId:guid}/summarize", async (Guid documentId, HttpContext http, AiAssistService svc, CancellationToken ct) =>
            Results.Ok(await svc.SummarizeDocumentAsync(documentId, IdempotencyKey(http), ct)));

        group.MapPost("/chat/channels/{channelId:guid}/summarize", async (Guid channelId, HttpContext http, AiAssistService svc, CancellationToken ct) =>
            Results.Ok(await svc.SummarizeChatAsync(channelId, IdempotencyKey(http), ct)));

        // Retrieval-augmented workspace Q&A and AI-ranked semantic search, both layered on top of
        // the already permission-filtered cross-module search fan-out (see WorkspaceQaService and
        // SemanticSearchService doc comments for the security rationale — never a parallel unfiltered path).
        group.MapPost("/ask", async (AskWorkspaceRequest r, WorkspaceQaService svc, CancellationToken ct) =>
            Results.Ok(await svc.AskAsync(r.Question, ct)));

        group.MapGet("/search/semantic", async (string? q, int? limit, SemanticSearchService svc, CancellationToken ct) =>
            Results.Ok(await svc.SearchAsync(q, limit, ct)));

        // Provider settings (Admin+, enforced by AiSettingsService).
        group.MapGet("/settings", async (AiSettingsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(ct)));

        group.MapPut("/settings", async (UpdateAiProviderSettingsRequest r, AiSettingsService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(ToCommand(r), ct)));

        group.MapPost("/settings/test", async (UpdateAiProviderSettingsRequest? r, AiSettingsService svc, CancellationToken ct) =>
            Results.Ok(await svc.TestAsync(ToCommand(r), ct)));

        // Per-workspace model allow-list + redaction configuration (Admin+).
        group.MapGet("/settings/governance", async (AiSettingsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetGovernanceAsync(ct)));

        group.MapPut("/settings/governance", async (UpdateAiGovernanceRequest r, AiSettingsService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateGovernanceAsync(
                new UpdateAiGovernanceCommand(r.AllowedModels, r.RedactEmails, r.RedactApiKeys, r.RedactCreditCards, r.CustomRedactionPatterns), ct)));
    }

    private static UpdateAiProviderSettingsCommand ToCommand(UpdateAiProviderSettingsRequest? r)
        => new(r?.BaseUrl ?? string.Empty, r?.Model ?? string.Empty, r?.ApiKey, r?.IsEnabled ?? false);

    private static void MapMobile(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/mobile").RequireAuthorization();

        group.MapGet("/devices", async (DeviceService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/devices", async (RegisterDeviceRequest r, DeviceService svc, CancellationToken ct) =>
        {
            var dto = await svc.RegisterAsync(new RegisterDeviceCommand(r.Platform, r.PushToken, r.AppVersion, r.Endpoint, r.P256dh, r.Auth), ct);
            return Results.Created($"/api/v1/mobile/devices/{dto.Id}", dto);
        });

        group.MapDelete("/devices/{id:guid}", async (Guid id, DeviceService svc, CancellationToken ct) =>
        {
            await svc.UnregisterAsync(id, ct);
            return Results.NoContent();
        });

        group.MapGet("/sync", async (DateTimeOffset? since, SyncService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetChangesAsync(since, ct)));

        // Gap-closer: the VAPID (RFC 8292) public key the frontend passes as
        // PushManager.subscribe({ applicationServerKey }). See VapidKeyProvider's doc comment: ephemeral,
        // regenerates every process restart, no workspace scoping needed since it isn't workspace data.
        group.MapGet("/push/vapid-public-key", (Planvexa.Api.Notifications.VapidKeyProvider vapid) =>
            Results.Ok(new { publicKey = vapid.PublicKeyBase64Url }));
    }

    private static void MapRetention(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/governance/retention-policy").RequireAuthorization();

        group.MapGet("/", async (RetentionService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(ct)));

        group.MapPut("/", async (UpdateRetentionPolicyRequest r, RetentionService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(new UpdateRetentionPolicyCommand(r.DeletedTaskRetentionDays, r.AuditRetentionDays, r.LegalHold), ct)));
    }

    private static string? IdempotencyKey(HttpContext http)
    {
        var key = http.Request.Headers["Idempotency-Key"].ToString();
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }
}
