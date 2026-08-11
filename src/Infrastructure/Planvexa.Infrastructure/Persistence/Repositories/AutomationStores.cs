namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Automations.Application;
using Planvexa.Modules.Automations.Domain;

internal sealed class AutomationRuleStore(PlanvexaDbContext db) : IAutomationRuleStore
{
    public void Add(AutomationRule rule) => db.Set<AutomationRule>().Add(rule);

    public void Remove(AutomationRule rule) => db.Set<AutomationRule>().Remove(rule);

    public Task<AutomationRule?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<AutomationRule>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<AutomationRule>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<AutomationRule>()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);

    public async Task<IReadOnlyList<AutomationRule>> ListEnabledByTriggerAsync(Guid workspaceId, string triggerType, CancellationToken ct = default)
        => await db.Set<AutomationRule>()
            .Where(x => x.WorkspaceId == workspaceId && x.IsEnabled && x.TriggerType == triggerType)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AutomationRule>> ListEnabledByTriggerAcrossWorkspacesAsync(string triggerType, CancellationToken ct = default)
        => await db.Set<AutomationRule>().IgnoreQueryFilters()
            .Where(x => x.IsEnabled && x.TriggerType == triggerType)
            .ToListAsync(ct);
}

internal sealed class AutomationRuleVersionStore(PlanvexaDbContext db) : IAutomationRuleVersionStore
{
    public void Add(AutomationRuleVersion version) => db.Set<AutomationRuleVersion>().Add(version);

    public async Task<IReadOnlyList<AutomationRuleVersion>> ListByRuleAsync(Guid ruleId, CancellationToken ct = default)
        => await db.Set<AutomationRuleVersion>()
            .Where(x => x.RuleId == ruleId)
            .OrderByDescending(x => x.Version)
            .ToListAsync(ct);

    public Task<AutomationRuleVersion?> FindAsync(Guid ruleId, int version, CancellationToken ct = default)
        => db.Set<AutomationRuleVersion>().FirstOrDefaultAsync(x => x.RuleId == ruleId && x.Version == version, ct);
}

internal sealed class AutomationRunStore(PlanvexaDbContext db) : IAutomationRunStore
{
    public void Add(AutomationRun run) => db.Set<AutomationRun>().Add(run);

    public Task<bool> ExistsAsync(Guid ruleId, Guid eventId, CancellationToken ct = default)
        => db.Set<AutomationRun>().AnyAsync(x => x.RuleId == ruleId && x.EventId == eventId, ct);

    public async Task<int> CountForWorkspaceSinceAsync(Guid workspaceId, DateTimeOffset sinceUtc, CancellationToken ct = default)
        => await db.Set<AutomationRun>()
            .CountAsync(x => x.WorkspaceId == workspaceId && x.OccurredAtUtc >= sinceUtc, ct);

    public async Task<IReadOnlyList<AutomationRun>> ListByRuleAsync(Guid ruleId, int max, CancellationToken ct = default)
        => await db.Set<AutomationRun>()
            .Where(x => x.RuleId == ruleId)
            .OrderByDescending(x => x.OccurredAtUtc).Take(max).ToListAsync(ct);

    public async Task<IReadOnlyList<AutomationRun>> ListDueForRetryAsync(DateTimeOffset nowUtc, int max, CancellationToken ct = default)
        => await db.Set<AutomationRun>().IgnoreQueryFilters()
            .Where(x => x.Status == AutomationRunStatus.Failed && x.NextRetryAtUtc != null && x.NextRetryAtUtc <= nowUtc)
            .OrderBy(x => x.NextRetryAtUtc)
            .Take(max)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AutomationRun>> ListDeadLettersAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<AutomationRun>()
            .Where(x => x.WorkspaceId == workspaceId && x.Status == AutomationRunStatus.DeadLetter)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ToListAsync(ct);

    public Task<AutomationRun?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<AutomationRun>().FirstOrDefaultAsync(x => x.Id == id, ct);
}
