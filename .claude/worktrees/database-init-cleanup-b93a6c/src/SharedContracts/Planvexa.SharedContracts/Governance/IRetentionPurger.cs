namespace Planvexa.SharedContracts.Governance;

/// <summary>
/// Contract (implemented in Infrastructure) that hard-deletes soft-deleted work items whose deletion is
/// older than the retention window, for the retention worker — without the Governance module touching
/// WorkManagement tables directly (AGENTS.md rule 7). Runs under the ambient tenant. The CALLER is
/// responsible for honoring legal hold (this only purges what it is asked to).
/// </summary>
public interface IRetentionPurger
{
    /// <summary>Count of soft-deleted work items in the tenant deleted before <paramref name="cutoffUtc"/>.</summary>
    Task<int> CountPurgeableAsync(Guid tenantId, DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes soft-deleted work items deleted before the cutoff. Returns the number removed.</summary>
    Task<int> PurgeAsync(Guid tenantId, DateTimeOffset cutoffUtc, int max, CancellationToken cancellationToken = default);
}
