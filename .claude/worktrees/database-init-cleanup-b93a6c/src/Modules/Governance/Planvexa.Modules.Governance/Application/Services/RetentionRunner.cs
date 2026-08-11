namespace Planvexa.Modules.Governance.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.Modules.Governance.Application;
using Planvexa.Modules.Governance.Domain;
using Planvexa.SharedContracts.Governance;

/// <summary>
/// Applies a workspace's retention policy by purging (hard-deleting) soft-deleted work items past the
/// retention window, via the <see cref="IRetentionPurger"/> contract. Invoked by the host background
/// worker under a bound workspace context. Legal hold or a zero/keep-forever window disables purging
/// (<see cref="RetentionPolicy.PurgeCutoff"/> returns null). Idempotent — only rows already soft-deleted
/// before the cutoff are removed, so repeated runs converge.
/// </summary>
public sealed class RetentionRunner(
    IRetentionPolicyStore policies,
    IRetentionPurger purger,
    IClock clock)
{
    private const int MaxPerRun = 500;

    /// <summary>Lists all workspace policies for the worker to iterate (cross-workspace read).</summary>
    public Task<IReadOnlyList<RetentionPolicy>> ListPoliciesAsync(CancellationToken ct = default)
        => policies.ListAllAsync(ct);

    /// <summary>Purges expired soft-deleted tasks for a workspace under the given policy. Returns the count removed.</summary>
    public async Task<int> ApplyAsync(RetentionPolicy policy, CancellationToken ct = default)
    {
        var cutoff = policy.PurgeCutoff(clock.UtcNow);
        if (cutoff is not { } cutoffUtc)
        {
            return 0; // Legal hold or keep-forever: nothing to purge.
        }

        return await purger.PurgeAsync(policy.WorkspaceId, cutoffUtc, MaxPerRun, ct);
    }
}
