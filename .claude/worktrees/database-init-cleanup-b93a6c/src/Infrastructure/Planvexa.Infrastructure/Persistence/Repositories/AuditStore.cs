namespace Planvexa.Infrastructure.Persistence.Repositories;

using Planvexa.Modules.Audit.Application;
using Planvexa.Modules.Audit.Domain;

internal sealed class AuditStore(PlanvexaDbContext db) : IAuditStore
{
    public void Add(AuditEvent auditEvent) => db.AuditEvents.Add(auditEvent);
}
