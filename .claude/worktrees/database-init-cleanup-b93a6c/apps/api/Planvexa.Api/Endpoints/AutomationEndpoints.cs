namespace Planvexa.Api.Endpoints;

using Planvexa.Modules.Automations.Application;
using Planvexa.Modules.Automations.Application.Services;
using Planvexa.Modules.Integrations.Application;
using Planvexa.Modules.Integrations.Application.Services;

// ---- Request models ----
public sealed record CreateAutomationRequest(string Name, string TriggerType, string? ConditionJson, string? ActionJson, string? TriggerConfigJson = null);
public sealed record UpdateAutomationRequest(string? Name, string? TriggerType, string? ConditionJson, string? ActionJson, string? TriggerConfigJson = null);
public sealed record DryRunAutomationRequest(IReadOnlyDictionary<string, string>? SampleEventData, Guid? SampleTaskId);
public sealed record CreateWebhookRequest(string Url, IReadOnlyList<string> EventTypes);
public sealed record CreateTokenRequest(string Name, IReadOnlyList<string>? Scopes, DateTimeOffset? ExpiresAtUtc);

/// <summary>Automation, webhook and personal-access-token endpoints.</summary>
public static class AutomationEndpoints
{
    public static void MapAutomationEndpoints(this RouteGroupBuilder api)
    {
        MapAutomations(api);
        MapWebhooks(api);
        MapTokens(api);
    }

    private static void MapAutomations(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/automations").RequireAuthorization();

        group.MapGet("/", async (AutomationRuleService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateAutomationRequest r, AutomationRuleService svc, CancellationToken ct) =>
        {
            var dto = await svc.CreateAsync(new CreateAutomationCommand(r.Name, r.TriggerType, r.ConditionJson, r.ActionJson, r.TriggerConfigJson), ct);
            return Results.Created($"/api/v1/automations/{dto.Id}", dto);
        });

        group.MapPatch("/{id:guid}", async (Guid id, UpdateAutomationRequest r, AutomationRuleService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(id, new UpdateAutomationCommand(r.Name, r.TriggerType, r.ConditionJson, r.ActionJson, r.TriggerConfigJson), ct)));

        group.MapPost("/{id:guid}/enable", async (Guid id, AutomationRuleService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetEnabledAsync(id, true, ct)));

        group.MapPost("/{id:guid}/disable", async (Guid id, AutomationRuleService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetEnabledAsync(id, false, ct)));

        group.MapDelete("/{id:guid}", async (Guid id, AutomationRuleService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/runs", async (Guid id, AutomationRuleService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListRunsAsync(id, ct)));

        // ---- templates ----
        group.MapGet("/templates", () =>
            Results.Ok(AutomationTemplates.All));

        group.MapPost("/templates/{key}/instantiate", async (string key, AutomationRuleService svc, CancellationToken ct) =>
        {
            var dto = await svc.CreateFromTemplateAsync(key, ct);
            return Results.Created($"/api/v1/automations/{dto.Id}", dto);
        });

        // ---- versioning ----
        group.MapGet("/{id:guid}/versions", async (Guid id, AutomationRuleService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListVersionsAsync(id, ct)));

        group.MapPost("/{id:guid}/versions/{version:int}/revert", async (Guid id, int version, AutomationRuleService svc, CancellationToken ct) =>
            Results.Ok(await svc.RevertToVersionAsync(id, version, ct)));

        // ---- dry-run ----
        group.MapPost("/{id:guid}/dry-run", async (Guid id, DryRunAutomationRequest r, AutomationRuleService svc, CancellationToken ct) =>
            Results.Ok(await svc.DryRunAsync(id, new DryRunAutomationCommand(r.SampleEventData, r.SampleTaskId), ct)));

        // ---- dead-letter ----
        group.MapGet("/dead-letters", async (AutomationRuleService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListDeadLettersAsync(ct)));

        group.MapPost("/runs/{runId:guid}/retry", async (Guid runId, AutomationRuleService svc, CancellationToken ct) =>
            Results.Ok(await svc.RetryRunAsync(runId, ct)));
    }

    private static void MapWebhooks(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/webhooks").RequireAuthorization();

        group.MapGet("/", async (WebhookService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateWebhookRequest r, WebhookService svc, CancellationToken ct) =>
        {
            var dto = await svc.CreateAsync(new CreateWebhookCommand(r.Url, r.EventTypes), ct);
            return Results.Created($"/api/v1/webhooks/{dto.Id}", dto);
        });

        group.MapDelete("/{id:guid}", async (Guid id, WebhookService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/deliveries", async (Guid id, WebhookService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListDeliveriesAsync(id, ct)));
    }

    private static void MapTokens(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/tokens").RequireAuthorization();

        group.MapGet("/", async (PersonalAccessTokenService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateTokenRequest r, PersonalAccessTokenService svc, CancellationToken ct) =>
        {
            var dto = await svc.CreateAsync(new CreateTokenCommand(r.Name, r.Scopes ?? Array.Empty<string>(), r.ExpiresAtUtc), ct);
            return Results.Created($"/api/v1/tokens/{dto.Id}", dto);
        });

        group.MapDelete("/{id:guid}", async (Guid id, PersonalAccessTokenService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });
    }
}
