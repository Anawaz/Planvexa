namespace Planvexa.Api.Endpoints;

using Planvexa.Modules.Governance.Application;
using Planvexa.Modules.Governance.Application.Services;

public sealed record UpdateSecuritySettingsRequest(bool? SsoEnabled, string? SamlEntityId, string? SamlMetadataUrl, bool? ScimEnabled, string? ScimToken, bool? MfaRequired);
public sealed record CreateExportRequest(string Dataset);
public sealed record AddIpAllowRuleRequest(string Cidr, string? Description);

/// <summary>Governance (audit/security/exports) endpoints.</summary>
public static class GovernanceEndpoints
{
    public static void MapGovernanceEndpoints(this RouteGroupBuilder api)
    {
        MapGovernance(api);
    }

    private static void MapGovernance(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/governance").RequireAuthorization();

        group.MapGet("/audit", async (string? action, string? entityType, Guid? actorUserId, DateTimeOffset? from, DateTimeOffset? to, AuditLogService svc, CancellationToken ct) =>
            Results.Ok(await svc.SearchAsync(action, entityType, actorUserId, from, to, ct)));

        group.MapGet("/audit/export", async (string? action, string? entityType, Guid? actorUserId, DateTimeOffset? from, DateTimeOffset? to, AuditLogService svc, CancellationToken ct) =>
        {
            var csv = await svc.ExportCsvAsync(action, entityType, actorUserId, from, to, ct);
            return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "audit-log.csv");
        });

        group.MapGet("/security-settings", async (SecuritySettingsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(ct)));

        group.MapPut("/security-settings", async (UpdateSecuritySettingsRequest r, SecuritySettingsService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(new UpdateSecuritySettingsCommand(r.SsoEnabled, r.SamlEntityId, r.SamlMetadataUrl, r.ScimEnabled, r.ScimToken, r.MfaRequired), ct)));

        group.MapGet("/exports", async (ExportJobService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/exports", async (CreateExportRequest r, ExportJobService svc, CancellationToken ct) =>
        {
            var dto = await svc.CreateAsync(r.Dataset, ct);
            return Results.Created($"/api/v1/governance/exports/{dto.Id}", dto);
        });

        group.MapGet("/exports/{id:guid}", async (Guid id, ExportJobService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(id, ct)));

        group.MapGet("/exports/{id:guid}/download", async (Guid id, ExportJobService svc, CancellationToken ct) =>
        {
            var download = await svc.DownloadAsync(id, ct);
            return Results.File(download.Content, download.ContentType, download.FileName);
        });

        // Per-workspace IP allow list (Admin+, same floor as every other governance
        // setting here). Enforcement itself happens in IpAllowListMiddleware, not here.
        group.MapGet("/ip-allow-rules", async (IpAllowListService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/ip-allow-rules", async (AddIpAllowRuleRequest r, IpAllowListService svc, CancellationToken ct) =>
        {
            var dto = await svc.AddAsync(new AddIpAllowRuleCommand(r.Cidr, r.Description), ct);
            return Results.Created($"/api/v1/governance/ip-allow-rules/{dto.Id}", dto);
        });

        group.MapDelete("/ip-allow-rules/{id:guid}", async (Guid id, IpAllowListService svc, CancellationToken ct) =>
        {
            await svc.RemoveAsync(id, ct);
            return Results.NoContent();
        });
    }
}
