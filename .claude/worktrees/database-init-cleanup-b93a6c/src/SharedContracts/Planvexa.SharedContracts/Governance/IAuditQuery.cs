namespace Planvexa.SharedContracts.Governance;

/// <summary>A read projection of an immutable audit event, for the governance audit-log UI/export.</summary>
public sealed record AuditRecord(
    Guid Id, Guid? ActorUserId, string Action, string EntityType, Guid? EntityId,
    string? IpAddress, DateTimeOffset CreatedAtUtc);

/// <summary>Filter for querying audit events. Null fields are ignored.</summary>
public sealed record AuditFilter(
    string? Action, string? EntityType, Guid? ActorUserId, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc,
    Guid? EntityId = null, int Max = 500);

/// <summary>
/// Contract (implemented in Infrastructure, which owns the DbContext) that lets the Governance module
/// read the immutable audit events for the audit-log UI and exports without touching the Audit module's
/// tables directly (mirrors the reporting-query pattern). Runs under the ambient tenant; the
/// tenant query filter provides isolation.
/// </summary>
public interface IAuditQuery
{
    Task<IReadOnlyList<AuditRecord>> SearchAsync(Guid tenantId, AuditFilter filter, CancellationToken cancellationToken = default);
}

/// <summary>
/// Contract (implemented in Infrastructure) exposing task rows for governed data export, without the
/// Governance module depending on WorkManagement internals. Runs under the ambient tenant.
/// </summary>
public interface IExportDataSource
{
    /// <summary>A flat row set (header + rows) for the given dataset in the workspace/tenant.</summary>
    Task<ExportRows> GetRowsAsync(Guid tenantId, string dataset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every entity type in the workspace, one named <see cref="ExportRows"/> table per entity, for the
    /// "full" governed export. Unlike <see cref="GetRowsAsync"/> this is not a single flat CSV shape —
    /// the caller (ExportRunner) writes each entry as its own file inside a zip archive.
    /// </summary>
    Task<IReadOnlyDictionary<string, ExportRows>> GetFullWorkspaceArchiveAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}

/// <summary>A simple tabular result: a header and string rows, ready to render as CSV.</summary>
public sealed record ExportRows(IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows);
