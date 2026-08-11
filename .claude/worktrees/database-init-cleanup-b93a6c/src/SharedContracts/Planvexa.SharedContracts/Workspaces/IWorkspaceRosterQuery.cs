namespace Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Cross-module contract (implemented by the Tenancy module) so other modules can enumerate a
/// workspace's active members without depending on Tenancy internals (AGENTS.md rule 7). Used by
/// schedulers that need to fan out to "everyone in the workspace" rather than a single caller, e.g.
/// the missing-time reminder.
/// </summary>
public interface IWorkspaceRosterQuery
{
    /// <summary>Active, non-guest members (WorkspaceRole >= Member) -- the same population TimeAuthorizer.CanTrackOwn allows.</summary>
    Task<IReadOnlyList<Guid>> ListActiveMemberUserIdsAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}
