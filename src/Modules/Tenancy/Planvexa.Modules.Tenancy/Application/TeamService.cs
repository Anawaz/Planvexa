namespace Planvexa.Modules.Tenancy.Application;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Tenancy.Authorization;
using Planvexa.Modules.Tenancy.Domain;

public sealed class TeamService(
    IWorkspaceContextAccessor workspaceAccessor,
    ITeamStore teams,
    IMembershipStore memberships,
    IWorkspaceStore workspaces,
    IRolePermissionResolver roleResolver,
    IIdGenerator ids,
    IClock clock,
    IAuditWriter audit,
    IUnitOfWork unitOfWork)
{
    public async Task<IReadOnlyList<TeamDto>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var workspace = await AuthorizeAsync(workspaceId, TenancyPermissions.MembersView, cancellationToken);
        var list = await teams.ListByWorkspaceAsync(workspace.Id, cancellationToken);
        var counts = await teams.CountMembersByTeamAsync(workspace.Id, cancellationToken);
        return list.Select(t => ToDto(t, counts.TryGetValue(t.Id, out var c) ? c : 0)).ToList();
    }

    public async Task<TeamDto> CreateAsync(CreateTeamCommand command, CancellationToken cancellationToken = default)
    {
        var workspace = await AuthorizeAsync(command.WorkspaceId, TenancyPermissions.MembersManage, cancellationToken);
        var team = Team.Create(ids.NewId(), workspace.Id, command.Name, command.Description, workspaceAccessor.Current.UserId, clock.UtcNow);
        teams.Add(team);
        audit.Write("team.created", nameof(Team), team.Id, new { command.Name, workspaceId = workspace.Id });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(team, 0);
    }

    public async Task<TeamDto> UpdateAsync(Guid teamId, UpdateTeamCommand command, CancellationToken cancellationToken = default)
    {
        var team = await LoadForManageAsync(teamId, cancellationToken);
        team.Update(command.Name, command.Description);
        audit.Write("team.updated", nameof(Team), team.Id, new { command.Name });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var count = await teams.ListMembersAsync(team.Id, cancellationToken);
        return ToDto(team, count.Count);
    }

    public async Task SetArchivedAsync(Guid teamId, bool archived, CancellationToken cancellationToken = default)
    {
        var team = await LoadForManageAsync(teamId, cancellationToken);
        if (archived)
        {
            team.Archive();
        }
        else
        {
            team.Restore();
        }

        audit.Write(archived ? "team.archived" : "team.restored", nameof(Team), team.Id, new { });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        var team = await LoadForManageAsync(teamId, cancellationToken);
        teams.Remove(team);
        audit.Write("team.deleted", nameof(Team), team.Id, new { });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TeamMemberDto>> ListMembersAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        RequireWorkspace();
        var team = await teams.FindAsync(teamId, cancellationToken) ?? throw new NotFoundException("Team not found.");
        await AuthorizeAsync(team.WorkspaceId, TenancyPermissions.MembersView, cancellationToken);
        var members = await teams.ListMembersAsync(teamId, cancellationToken);
        return members.Select(m => new TeamMemberDto(m.UserId, m.AddedAtUtc)).ToList();
    }

    public async Task AddMemberAsync(Guid teamId, Guid userId, CancellationToken cancellationToken = default)
    {
        var team = await LoadForManageAsync(teamId, cancellationToken);

        var member = await memberships.FindAsync(team.WorkspaceId, userId, cancellationToken);
        if (member is null || member.Status != MembershipStatus.Active)
        {
            throw new ValidationAppException("Only active workspace members can be added to a team.");
        }

        if (await teams.FindMemberAsync(teamId, userId, cancellationToken) is not null)
        {
            return; // Idempotent.
        }

        teams.AddMember(TeamMembership.Create(ids.NewId(), team.WorkspaceId, teamId, userId, clock.UtcNow));
        audit.Write("team.member_added", nameof(Team), team.Id, new { userId });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveMemberAsync(Guid teamId, Guid userId, CancellationToken cancellationToken = default)
    {
        var team = await LoadForManageAsync(teamId, cancellationToken);
        var membership = await teams.FindMemberAsync(teamId, userId, cancellationToken)
            ?? throw new NotFoundException("This user is not on the team.");
        teams.RemoveMember(membership);
        audit.Write("team.member_removed", nameof(Team), team.Id, new { userId });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Team> LoadForManageAsync(Guid teamId, CancellationToken cancellationToken)
    {
        RequireWorkspace();
        var team = await teams.FindAsync(teamId, cancellationToken) ?? throw new NotFoundException("Team not found.");
        await AuthorizeAsync(team.WorkspaceId, TenancyPermissions.MembersManage, cancellationToken);
        return team;
    }

    private async Task<Domain.Workspace> AuthorizeAsync(
        Guid workspaceId, string permission, CancellationToken cancellationToken)
    {
        var ctx = RequireWorkspace();
        var workspace = await workspaces.FindByIdAsync(workspaceId, cancellationToken)
            ?? throw new NotFoundException("Workspace not found.");

        var direct = await memberships.FindAsync(workspace.Id, ctx.UserId, cancellationToken);
        var permissions = await roleResolver.ResolveAsync(direct, cancellationToken);
        TenancyAuthorizer.Ensure(permissions, permission);
        return workspace;
    }

    private IWorkspaceContext RequireWorkspace()
    {
        var ctx = workspaceAccessor.Current;
        if (!ctx.HasWorkspace)
        {
            throw new ForbiddenException("A workspace context is required for this operation.");
        }

        return ctx;
    }

    private static TeamDto ToDto(Team t, int memberCount)
        => new(t.Id, t.WorkspaceId, t.Name, t.Description, t.IsArchived, memberCount);
}
