namespace Planvexa.Modules.Tenancy.Application;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Tenancy.Authorization;
using Planvexa.Modules.Tenancy.Domain;

public sealed class InvitationService(
    IWorkspaceContextAccessor workspaceAccessor,
    IInvitationStore invitations,
    IMembershipStore memberships,
    IWorkspaceStore workspaces,
    IRoleStore roles,
    IRolePermissionResolver roleResolver,
    IIdGenerator ids,
    IClock clock,
    IAuditWriter audit,
    IInvitationEmailSender invitationEmail,
    IUnitOfWork unitOfWork)
{
    private static readonly TimeSpan InvitationValidity = TimeSpan.FromDays(14);

    public async Task<InvitationCreatedDto> InviteAsync(InviteMemberCommand command, CancellationToken cancellationToken = default)
    {
        var ctx = workspaceAccessor.Current;
        if (!ctx.HasWorkspace)
        {
            throw new ForbiddenException("A workspace context is required for this operation.");
        }

        var workspace = await workspaces.FindByIdAsync(command.WorkspaceId, cancellationToken)
            ?? throw new NotFoundException("Workspace not found.");

        var callerMembership = await memberships.FindAsync(workspace.Id, ctx.UserId, cancellationToken);
        var callerPermissions = await roleResolver.ResolveAsync(callerMembership, cancellationToken);
        TenancyAuthorizer.Ensure(callerPermissions, TenancyPermissions.MembersInvite);

        var email = command.Email.Trim().ToLowerInvariant();
        var existing = await invitations.FindPendingAsync(workspace.Id, email, cancellationToken);
        if (existing is not null)
        {
            throw new ConflictException("A pending invitation already exists for this email.");
        }

        var now = clock.UtcNow;
        var (invitation, rawToken) = Invitation.Create(
            ids.NewId(), workspace.Id, email, command.Role, ctx.UserId, now, InvitationValidity);

        invitations.Add(invitation);
        audit.Write("member.invited", nameof(Invitation), invitation.Id, new { email, role = command.Role.ToString(), workspaceId = workspace.Id });

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Deliver the raw token out-of-band as a signed link. It leaves the process only via email;
        // the API response below carries no token (AGENTS.md invitation security).
        await invitationEmail.SendInvitationAsync(new InvitationEmailMessage(
            workspace.Id, invitation.Id, workspace.Name, invitation.Email,
            invitation.Role.ToString(), rawToken, invitation.ExpiresAtUtc), cancellationToken);

        return new InvitationCreatedDto(invitation.Id, invitation.Email, invitation.Role.ToString(), invitation.ExpiresAtUtc);
    }

    /// <summary>
    /// Accepts an invitation using its raw token. Starts WITHOUT an ambient Workspace context: the
    /// token is the credential and the invitation carries the target Workspace. Once the invitation is
    /// resolved the ambient Workspace is bound to it, so the membership write (and the RLS policies
    /// behind it) are scoped to the Workspace the token proves.
    /// </summary>
    public async Task<InvitationAcceptedDto> AcceptAsync(string rawToken, Guid userId, CancellationToken cancellationToken = default)
    {
        var tokenHash = Invitation.HashToken(rawToken);
        var invitation = await invitations.FindByTokenHashAsync(tokenHash, cancellationToken)
            ?? throw new NotFoundException("Invitation not found.");

        workspaceAccessor.Set(new WorkspaceContext(
            workspaceId: invitation.WorkspaceId,
            userId: userId,
            membershipId: null,
            role: string.Empty,
            permissions: new HashSet<string>(),
            entitlements: new HashSet<string>(),
            correlationId: workspaceAccessor.Current.CorrelationId));

        var now = clock.UtcNow;
        if (invitation.Status != InvitationStatus.Pending)
        {
            throw new ConflictException("This invitation is no longer valid.");
        }

        if (invitation.IsExpired(now))
        {
            invitation.MarkExpired();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new ConflictException("This invitation has expired.");
        }

        var existingMembership = await memberships.FindAsync(invitation.WorkspaceId, userId, cancellationToken);
        if (existingMembership is not null)
        {
            invitation.Accept(userId, existingMembership.Id, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new InvitationAcceptedDto(existingMembership.Id, invitation.WorkspaceId, existingMembership.Role.ToString());
        }

        var invitedRole = await roles.FindByKeyAsync(invitation.WorkspaceId, BuiltInRoles.KeyFor(invitation.Role), cancellationToken);
        var membership = WorkspaceMember.Create(
            ids.NewId(), invitation.WorkspaceId, userId, invitation.Role, now, invitedRole?.Id);
        memberships.Add(membership);
        invitation.Accept(userId, membership.Id, now);

        audit.Write("invitation.accepted", nameof(Invitation), invitation.Id, new { userId, membershipId = membership.Id });

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new InvitationAcceptedDto(membership.Id, invitation.WorkspaceId, membership.Role.ToString());
    }

    public async Task<IReadOnlyList<PendingInvitationDto>> ListPendingAsync(
        Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var workspace = await AuthorizeAsync(workspaceId, TenancyPermissions.MembersView, cancellationToken);
        var pending = await invitations.ListPendingByWorkspaceAsync(workspace.Id, cancellationToken);
        return pending
            .Select(i => new PendingInvitationDto(
                i.Id, i.Email, i.Role.ToString(), i.Status.ToString(), i.CreatedAtUtc, i.ExpiresAtUtc))
            .ToList();
    }

    /// <summary>Rotates the invitation token (invalidating the old link) and re-issues it.</summary>
    public async Task<InvitationCreatedDto> ResendAsync(
        Guid workspaceId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        var workspace = await AuthorizeAsync(workspaceId, TenancyPermissions.MembersInvite, cancellationToken);
        var invitation = await invitations.FindByIdAsync(workspace.Id, invitationId, cancellationToken)
            ?? throw new NotFoundException("Invitation not found.");
        if (invitation.Status == InvitationStatus.Accepted)
        {
            throw new ConflictException("This invitation has already been accepted.");
        }

        var rawToken = invitation.Rotate(clock.UtcNow, InvitationValidity);
        audit.Write("member.invitation_resent", nameof(Invitation), invitation.Id,
            new { invitation.Email, workspaceId = workspace.Id });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Rotating invalidates the old link; deliver the new one by email. No token in the response.
        await invitationEmail.SendInvitationAsync(new InvitationEmailMessage(
            workspace.Id, invitation.Id, workspace.Name, invitation.Email,
            invitation.Role.ToString(), rawToken, invitation.ExpiresAtUtc), cancellationToken);

        return new InvitationCreatedDto(invitation.Id, invitation.Email, invitation.Role.ToString(), invitation.ExpiresAtUtc);
    }

    public async Task RevokeAsync(Guid workspaceId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        var workspace = await AuthorizeAsync(workspaceId, TenancyPermissions.MembersInvite, cancellationToken);
        var invitation = await invitations.FindByIdAsync(workspace.Id, invitationId, cancellationToken)
            ?? throw new NotFoundException("Invitation not found.");
        if (invitation.Status != InvitationStatus.Pending)
        {
            throw new ConflictException("Only a pending invitation can be revoked.");
        }

        invitation.Revoke();
        audit.Write("member.invitation_revoked", nameof(Invitation), invitation.Id,
            new { invitation.Email, workspaceId = workspace.Id });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Domain.Workspace> AuthorizeAsync(
        Guid workspaceId, string permission, CancellationToken cancellationToken)
    {
        var ctx = workspaceAccessor.Current;
        if (!ctx.HasWorkspace)
        {
            throw new ForbiddenException("A workspace context is required for this operation.");
        }

        var workspace = await workspaces.FindByIdAsync(workspaceId, cancellationToken)
            ?? throw new NotFoundException("Workspace not found.");

        var callerMembership = await memberships.FindAsync(workspace.Id, ctx.UserId, cancellationToken);
        var permissions = await roleResolver.ResolveAsync(callerMembership, cancellationToken);
        TenancyAuthorizer.Ensure(permissions, permission);
        return workspace;
    }
}
