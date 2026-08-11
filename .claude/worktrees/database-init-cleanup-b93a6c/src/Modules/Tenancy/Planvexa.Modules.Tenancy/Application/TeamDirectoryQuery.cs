namespace Planvexa.Modules.Tenancy.Application;

using Planvexa.SharedContracts.Teams;

/// <summary>
/// Cross-module implementation of <see cref="ITeamDirectoryQuery"/> (AGENTS.md rule 7) -- backs
/// Planning's Team view with Tenancy's Team/TeamMembership data without another module reading
/// Tenancy's tables directly. Archived teams are excluded (same convention as TeamService.ListAsync).
/// </summary>
public sealed class TeamDirectoryQuery(ITeamStore teams) : ITeamDirectoryQuery
{
    public async Task<IReadOnlyList<TeamSummary>> ListTeamsAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var all = await teams.ListByWorkspaceAsync(workspaceId, ct);
        var result = new List<TeamSummary>();
        foreach (var team in all.Where(t => !t.IsArchived))
        {
            var members = await teams.ListMembersAsync(team.Id, ct);
            result.Add(new TeamSummary(team.Id, team.Name, members.Select(m => m.UserId).ToList()));
        }

        return result;
    }
}
