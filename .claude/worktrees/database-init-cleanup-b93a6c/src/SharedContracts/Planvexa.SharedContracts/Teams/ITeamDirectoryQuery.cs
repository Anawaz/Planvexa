namespace Planvexa.SharedContracts.Teams;

/// <summary>One workspace Team and the user ids currently on it.</summary>
public sealed record TeamSummary(Guid Id, string Name, IReadOnlyList<Guid> MemberUserIds);

/// <summary>
/// Cross-module read of a workspace's Teams (Tenancy owns the Team/TeamMembership tables -- see
/// AGENTS.md rule 7: cross-module dependencies go through a SharedContracts contract, never a direct
/// table read). Backs the Planning module's Team view.
/// </summary>
public interface ITeamDirectoryQuery
{
    Task<IReadOnlyList<TeamSummary>> ListTeamsAsync(Guid workspaceId, CancellationToken ct = default);
}
