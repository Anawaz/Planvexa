namespace Planvexa.Modules.Governance.Application.Services;

using System.Globalization;
using Planvexa.Modules.Governance.Application;
using Planvexa.Modules.Governance.Authorization;
using Planvexa.SharedContracts.Governance;

/// <summary>Queries and exports immutable audit events for governance administrators.</summary>
public sealed class AuditLogService(
    GovernanceServiceContext ctx,
    IAuditQuery audit)
    : GovernanceServiceBase(ctx)
{
    public async Task<IReadOnlyList<AuditEntryDto>> SearchAsync(
        string? action,
        string? entityType,
        Guid? actorUserId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GovernanceAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var records = await audit.SearchAsync(workspaceId, new AuditFilter(action, entityType, actorUserId, from, to), ct);
        return records.Select(ToDto).ToList();
    }

    public async Task<string> ExportCsvAsync(
        string? action,
        string? entityType,
        Guid? actorUserId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GovernanceAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var records = await audit.SearchAsync(workspaceId, new AuditFilter(action, entityType, actorUserId, from, to), ct);
        var rows = records.Select(r => new[]
        {
            r.Id.ToString(),
            r.ActorUserId?.ToString() ?? string.Empty,
            r.Action,
            r.EntityType,
            r.EntityId?.ToString() ?? string.Empty,
            r.IpAddress ?? string.Empty,
            r.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        });

        return CsvWriter.Write(
            new[] { "id", "actorUserId", "action", "entityType", "entityId", "ipAddress", "createdAtUtc" },
            rows);
    }

    private static AuditEntryDto ToDto(AuditRecord r)
        => new(r.Id, r.ActorUserId, r.Action, r.EntityType, r.EntityId, r.IpAddress, r.CreatedAtUtc);
}

