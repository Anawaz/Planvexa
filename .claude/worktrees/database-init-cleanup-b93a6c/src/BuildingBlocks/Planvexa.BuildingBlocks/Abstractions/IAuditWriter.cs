namespace Planvexa.BuildingBlocks.Abstractions;

/// <summary>
/// Stages an audit event for the current operation. The event is persisted when the surrounding
/// unit of work commits, so the audit record and the business change share one transaction
/// (AGENTS.md rule 12). Cross-cutting capability — the implementation lives in the Audit module.
/// </summary>
public interface IAuditWriter
{
    void Write(string action, string entityType, Guid? entityId = null, object? data = null, string? ipAddress = null);
}
