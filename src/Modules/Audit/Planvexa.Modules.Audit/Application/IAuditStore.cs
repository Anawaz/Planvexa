namespace Planvexa.Modules.Audit.Application;

using Planvexa.Modules.Audit.Domain;

/// <summary>Persistence abstraction for audit events (implemented in Infrastructure).</summary>
public interface IAuditStore
{
    void Add(AuditEvent auditEvent);
}
