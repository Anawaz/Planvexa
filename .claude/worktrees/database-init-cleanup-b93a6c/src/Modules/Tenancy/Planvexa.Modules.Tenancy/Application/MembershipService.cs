namespace Planvexa.Modules.Tenancy.Application;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Tenancy.Authorization;
using Planvexa.Modules.Tenancy.Domain;

public sealed class MembershipService(
    IWorkspaceContextAccessor workspaceAccessor,
    IMembershipStore memberships,
    IWorkspaceStore workspaces,
    IRoleStore roles,
    IRolePermissionResolver roleResolver,
    IAuditWriter audit,
    IUnitOfWork unitOfWork)
{
    public async Task<IReadOnlyList<MemberDto>> ListWorkspaceMembersAsync(
        Guid workspaceId, CancellationToken cancellationToken = default)
    {
        RequireWorkspace();
        var workspace = await workspaces.FindByIdAsync(workspaceId, cancellationToken)
            ?? throw new NotFoundException("Workspace not found.");

        var caller = await memberships.FindAsync(workspace.Id, workspaceAccessor.Current.UserId, cancellationToken);
        var permissions = await roleResolver.ResolveAsync(caller, cancellationToken);
        TenancyAuthorizer.Ensure(permissions, TenancyPermissions.MembersView);

        var members = await memberships.ListByWorkspaceAsync(workspaceId, cancellationToken);
        return members
            .Select(ToDto)
            .ToList();
    }

    public async Task<MemberDto> ChangeRoleAsync(ChangeMemberRoleCommand command, CancellationToken cancellationToken = default)
    {
        var (workspace, member) = await ResolveManageableMemberAsync(command.WorkspaceId, command.MembershipId, cancellationToken);

        // Protect the last Owner: demoting them would leave the workspace without an owner.
        if (member.Role == MembershipRole.Owner && command.Role != MembershipRole.Owner
            && await memberships.CountActiveOwnersAsync(workspace.Id, cancellationToken) <= 1)
        {
            throw new ConflictException("The last Owner cannot be demoted. Assign another Owner first.");
        }

        var targetRole = await roles.FindByKeyAsync(workspace.Id, BuiltInRoles.KeyFor(command.Role), cancellationToken);
        member.ChangeRole(command.Role, targetRole?.Id);
        audit.Write("member.role_changed", nameof(WorkspaceMember), member.Id,
            new { workspaceId = workspace.Id, member.UserId, role = command.Role.ToString(), roleId = member.RoleId });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(member);
    }

    public async Task<MemberDto> DeactivateAsync(Guid workspaceId, Guid membershipId, CancellationToken cancellationToken = default)
    {
        var (workspace, member) = await ResolveManageableMemberAsync(workspaceId, membershipId, cancellationToken);

        if (member.Role == MembershipRole.Owner && member.Status == MembershipStatus.Active
            && await memberships.CountActiveOwnersAsync(workspace.Id, cancellationToken) <= 1)
        {
            throw new ConflictException("The last Owner cannot be deactivated. Assign another Owner first.");
        }

        member.Deactivate();
        audit.Write("member.deactivated", nameof(WorkspaceMember), member.Id, new { workspaceId = workspace.Id, member.UserId });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(member);
    }

    public async Task<MemberDto> ReactivateAsync(Guid workspaceId, Guid membershipId, CancellationToken cancellationToken = default)
    {
        var (workspace, member) = await ResolveManageableMemberAsync(workspaceId, membershipId, cancellationToken);

        member.Reactivate();
        audit.Write("member.reactivated", nameof(WorkspaceMember), member.Id, new { workspaceId = workspace.Id, member.UserId });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(member);
    }

    /// <summary>The caller removes their own membership. The last Owner must transfer ownership first.</summary>
    public async Task LeaveAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var userId = RequireWorkspace().UserId;
        var workspace = await workspaces.FindByIdAsync(workspaceId, cancellationToken)
            ?? throw new NotFoundException("Workspace not found.");

        var member = await memberships.FindAsync(workspace.Id, userId, cancellationToken)
            ?? throw new NotFoundException("You are not a member of this workspace.");

        if (member.Role == MembershipRole.Owner
            && await memberships.CountActiveOwnersAsync(workspace.Id, cancellationToken) <= 1)
        {
            throw new ConflictException("Transfer ownership before leaving \u2014 a workspace must always have an Owner.");
        }

        memberships.Remove(member);
        audit.Write("member.left", nameof(WorkspaceMember), member.Id, new { workspaceId = workspace.Id, member.UserId });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Promotes the target member to Owner and demotes the caller to Admin. Owner-only.</summary>
    public async Task TransferOwnershipAsync(TransferOwnershipCommand command, CancellationToken cancellationToken = default)
    {
        var userId = RequireWorkspace().UserId;
        var workspace = await workspaces.FindByIdAsync(command.WorkspaceId, cancellationToken)
            ?? throw new NotFoundException("Workspace not found.");

        var caller = await memberships.FindAsync(workspace.Id, userId, cancellationToken);
        if (caller?.Role != MembershipRole.Owner)
        {
            throw new ForbiddenException("Only an Owner can transfer ownership.");
        }

        var target = await memberships.FindByIdAsync(workspace.Id, command.MembershipId, cancellationToken)
            ?? throw new NotFoundException("Member not found.");
        if (target.Status != MembershipStatus.Active)
        {
            throw new ConflictException("Ownership can only be transferred to an active member.");
        }

        var ownerRole = await roles.FindByKeyAsync(workspace.Id, BuiltInRoles.KeyFor(MembershipRole.Owner), cancellationToken);
        target.ChangeRole(MembershipRole.Owner, ownerRole?.Id);
        if (caller is not null && caller.Id != target.Id)
        {
            var adminRole = await roles.FindByKeyAsync(workspace.Id, BuiltInRoles.KeyFor(MembershipRole.Admin), cancellationToken);
            caller.ChangeRole(MembershipRole.Admin, adminRole?.Id);
        }

        audit.Write("workspace.ownership_transferred", nameof(WorkspaceMember), target.Id,
            new { workspaceId = workspace.Id, fromUserId = userId, toUserId = target.UserId });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<(Domain.Workspace Workspace, WorkspaceMember Member)> ResolveManageableMemberAsync(
        Guid workspaceId, Guid membershipId, CancellationToken cancellationToken)
    {
        var userId = RequireWorkspace().UserId;
        var workspace = await workspaces.FindByIdAsync(workspaceId, cancellationToken)
            ?? throw new NotFoundException("Workspace not found.");

        var caller = await memberships.FindAsync(workspace.Id, userId, cancellationToken);
        var permissions = await roleResolver.ResolveAsync(caller, cancellationToken);
        TenancyAuthorizer.Ensure(permissions, TenancyPermissions.MembersManage);

        var member = await memberships.FindByIdAsync(workspace.Id, membershipId, cancellationToken)
            ?? throw new NotFoundException("Member not found.");
        return (workspace, member);
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

    private static MemberDto ToDto(WorkspaceMember m)
        => new(m.Id, m.UserId, m.Role.ToString(), m.Status.ToString(), m.IsGuest, m.JoinedAtUtc);
}
