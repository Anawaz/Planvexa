namespace Planvexa.Modules.Automations.Application;

using Planvexa.Modules.Automations.Domain;

public interface IAutomationRuleStore
{
    void Add(AutomationRule rule);
    void Remove(AutomationRule rule);
    Task<AutomationRule?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AutomationRule>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task<IReadOnlyList<AutomationRule>> ListEnabledByTriggerAsync(Guid workspaceId, string triggerType, CancellationToken ct = default);

    /// <summary>Every enabled rule of the given trigger type across ALL workspaces — used by the
    /// scheduled/due-date/SLA background sweeps (mirrors ScheduledReportBackgroundService's cross-workspace
    /// "list enabled, filter due" pattern). Not workspace-scoped by design.</summary>
    Task<IReadOnlyList<AutomationRule>> ListEnabledByTriggerAcrossWorkspacesAsync(string triggerType, CancellationToken ct = default);
}

public interface IAutomationRuleVersionStore
{
    void Add(AutomationRuleVersion version);
    Task<IReadOnlyList<AutomationRuleVersion>> ListByRuleAsync(Guid ruleId, CancellationToken ct = default);
    Task<AutomationRuleVersion?> FindAsync(Guid ruleId, int version, CancellationToken ct = default);
}

public interface IAutomationRunStore
{
    void Add(AutomationRun run);
    Task<bool> ExistsAsync(Guid ruleId, Guid eventId, CancellationToken ct = default);
    Task<int> CountForWorkspaceSinceAsync(Guid workspaceId, DateTimeOffset sinceUtc, CancellationToken ct = default);
    Task<IReadOnlyList<AutomationRun>> ListByRuleAsync(Guid ruleId, int max, CancellationToken ct = default);

    /// <summary>retries: Failed runs across all workspaces whose NextRetryAtUtc has arrived —
    /// used by the retry background sweep.</summary>
    Task<IReadOnlyList<AutomationRun>> ListDueForRetryAsync(DateTimeOffset nowUtc, int max, CancellationToken ct = default);

    /// <summary>dead-letter: dead-lettered runs for a workspace, newest first.</summary>
    Task<IReadOnlyList<AutomationRun>> ListDeadLettersAsync(Guid workspaceId, CancellationToken ct = default);

    Task<AutomationRun?> FindAsync(Guid id, CancellationToken ct = default);
}
