namespace Planvexa.Modules.Audit.Application;

using System.Text.Json;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Audit.Domain;

/// <summary>Implements <see cref="IAuditWriter"/> by staging an <see cref="AuditEvent"/> in the unit of work.</summary>
public sealed class AuditWriter(
    IAuditStore store,
    IWorkspaceContextAccessor workspaceAccessor,
    ICurrentUser currentUser,
    IIdGenerator ids,
    IClock clock) : IAuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Write(string action, string entityType, Guid? entityId = null, object? data = null, string? ipAddress = null)
    {
        var workspace = workspaceAccessor.Current;
        var payload = data is null ? null : JsonSerializer.Serialize(data, JsonOptions);

        var auditEvent = new AuditEvent(
            id: ids.NewId(),
            workspaceId: workspace.HasWorkspace ? workspace.WorkspaceId : null,
            actorUserId: currentUser.IsAuthenticated ? currentUser.UserId : null,
            action: action,
            entityType: entityType,
            entityId: entityId,
            data: payload,
            correlationId: workspace.HasWorkspace ? workspace.CorrelationId : null,
            ipAddress: ipAddress,
            createdAtUtc: clock.UtcNow);

        store.Add(auditEvent);
    }
}
