namespace Planvexa.Modules.Planning.Application.Services;

using Planvexa.SharedContracts.Teams;

public sealed record TeamWorkloadMemberDto(Guid UserId, decimal CapacityHours, decimal ScheduledHours, decimal LoggedHours, bool IsOverAllocated);

public sealed record TeamWorkloadRowDto(
    Guid TeamId, string TeamName, decimal CapacityHours, decimal ScheduledHours, decimal LoggedHours,
    IReadOnlyList<TeamWorkloadMemberDto> Members);

/// <summary>
/// Team view -- the same per-member workload <see cref="WorkloadService"/> already computes
/// (capacity/scheduled/logged hours), grouped by Team instead of shown flat per-individual. Team
/// membership is read cross-module via <see cref="ITeamDirectoryQuery"/> (Tenancy owns Team data --
/// AGENTS.md rule 7); authorization is inherited from <see cref="WorkloadService.ComputeAsync"/>
/// (Admin+ "manage" gate, same as the existing per-member Workload view).
/// </summary>
public sealed class TeamWorkloadService(PlanningServiceContext ctx, WorkloadService workload, ITeamDirectoryQuery teams)
    : PlanningServiceBase(ctx)
{
    public async Task<IReadOnlyList<TeamWorkloadRowDto>> ComputeAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();

        // WorkloadService.ComputeAsync performs the PlanningAuthorizer.EnsureManage role check; letting
        // it run first means an unauthorized caller never reaches the Team read below.
        var memberRows = (await workload.ComputeAsync(fromUtc, toUtc, ct)).ToDictionary(r => r.UserId);
        var teamSummaries = await teams.ListTeamsAsync(workspaceId, ct);

        var result = new List<TeamWorkloadRowDto>(teamSummaries.Count);
        foreach (var team in teamSummaries)
        {
            var members = team.MemberUserIds
                .Select(userId => memberRows.TryGetValue(userId, out var row)
                    ? new TeamWorkloadMemberDto(userId, row.CapacityHours, row.ScheduledHours, row.LoggedHours, row.IsOverAllocated)
                    : new TeamWorkloadMemberDto(userId, 0m, 0m, 0m, false))
                .ToList();

            result.Add(new TeamWorkloadRowDto(
                team.Id, team.Name,
                members.Sum(m => m.CapacityHours), members.Sum(m => m.ScheduledHours), members.Sum(m => m.LoggedHours),
                members));
        }

        return result.OrderByDescending(r => r.ScheduledHours).ToList();
    }
}
