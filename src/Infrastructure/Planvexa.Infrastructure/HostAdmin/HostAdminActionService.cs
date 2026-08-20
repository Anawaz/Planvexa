namespace Planvexa.Infrastructure.HostAdmin;

using Microsoft.EntityFrameworkCore;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Infrastructure.Persistence;
using Planvexa.Infrastructure.Persistence.Repositories;
using Planvexa.Modules.Identity.Application;
using Planvexa.Modules.Tenancy.Application;
using Planvexa.Modules.Tenancy.Domain;

/// <summary>
/// Host-administrator state changes: suspending/restoring/deleting a Workspace, and
/// disabling/enabling/promoting/demoting an account.
///
/// Authorization is entirely the <c>HostAdmin</c> endpoint policy plus the host-admin RLS policies
/// (script 0094) — this class assumes the caller has already been established as a host admin and
/// concerns itself only with the invariants an authorized host admin can still get wrong (locking
/// every administrator out of the installation).
///
/// Every action writes an audit event, and the <c>host.</c> action-name prefix is what marks it as
/// instance-level. The <c>WorkspaceId</c> column follows from which kind of action it is:
/// <list type="bullet">
/// <item>Workspace-targeted (suspend/restore/delete) events carry the target WorkspaceId. Not a
/// choice — those actions bind the scope to the target workspace (see <c>EnterWorkspace</c>), so the
/// <c>audit_isolation</c> WITH CHECK (0029) requires the row to match the ambient workspace and a null
/// would be rejected outright. It is also the more useful record: the workspace's own owners see in
/// their audit log that it was suspended, and the host console sees it either way.</item>
/// <item>Account-targeted (disable/enable/promote/demote) events carry null, the documented meaning of
/// that column ("null for platform-level events"), because they are not about any one Workspace and
/// run with no ambient Workspace at all.</item>
/// </list>
/// </summary>
public sealed class HostAdminActionService(
    PlanvexaDbContext db,
    IWorkspaceContextAccessor workspaceAccessor,
    IUserStore users,
    ICurrentUser currentUser,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    IClock clock,
    WorkspaceDeletionService deletion)
{
    public async Task<string> SuspendWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        => await SetWorkspaceStatusAsync(workspaceId, suspend: true, cancellationToken);

    public async Task<string> RestoreWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        => await SetWorkspaceStatusAsync(workspaceId, suspend: false, cancellationToken);

    /// <summary>
    /// Permanently deletes a Workspace and everything in it. Delegates to the same
    /// <see cref="WorkspaceDeletionService"/> the Owner-facing path uses (cascade delete + blob sweep
    /// + pre-committed audit event), so there is exactly one deletion implementation to keep correct.
    /// </summary>
    public async Task DeleteWorkspaceAsync(Guid workspaceId, string confirmSlug, CancellationToken cancellationToken = default)
    {
        EnterWorkspace(workspaceId);
        await deletion.DeleteAsHostAdminAsync(workspaceId, confirmSlug, cancellationToken);
    }

    public async Task SetUserActiveAsync(Guid userId, bool active, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId, cancellationToken);

        if (!active)
        {
            // Both guards exist for the same reason: an installation must never end up with no way in.
            // Self-disable is the fast way to lock yourself out; disabling the last remaining host
            // admin is the slow way (and would leave the console reachable by nobody at all).
            if (userId == currentUser.UserId)
            {
                throw new ConflictException("You cannot disable your own account.");
            }

            if (user.IsHostAdmin && await users.CountHostAdminsAsync(cancellationToken) <= 1)
            {
                throw new ConflictException(
                    "This is the only host administrator. Grant host administration to another account first.");
            }

            user.Deactivate(clock.UtcNow);
        }
        else
        {
            user.Reactivate(clock.UtcNow);
        }

        audit.Write(active ? "host.user.enabled" : "host.user.disabled", "User", user.Id, new { user.Email });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task SetHostAdminAsync(Guid userId, bool granted, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId, cancellationToken);

        if (granted)
        {
            if (!user.IsActive)
            {
                throw new ConflictException("A disabled account cannot be made a host administrator. Enable it first.");
            }

            user.GrantHostAdmin(clock.UtcNow);
        }
        else
        {
            if (userId == currentUser.UserId)
            {
                throw new ConflictException(
                    "You cannot revoke your own host administration. Ask another host administrator to do it.");
            }

            if (user.IsHostAdmin && await users.CountHostAdminsAsync(cancellationToken) <= 1)
            {
                throw new ConflictException("This is the only host administrator; there would be none left.");
            }

            user.RevokeHostAdmin(clock.UtcNow);
        }

        audit.Write(
            granted ? "host.user.host_admin_granted" : "host.user.host_admin_revoked",
            "User", user.Id, new { user.Email });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // ---- internals ----

    private async Task<string> SetWorkspaceStatusAsync(Guid workspaceId, bool suspend, CancellationToken cancellationToken)
    {
        EnterWorkspace(workspaceId);

        // Stamped-connection read for the same reason WorkspaceStore.FindByIdAsync uses one: Workspace
        // carries no EF-side query filter, so its visibility depends entirely on the Postgres session
        // variables being current on THIS physical connection.
        var workspace = await TenancySessionGuard.WithStampedWorkspaceAsync(
            db, workspaceId,
            () => db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken),
            cancellationToken)
            ?? throw new NotFoundException("Workspace not found.");

        // Archived is the product's existing "nobody may enter" state: WorkspaceResolver already
        // refuses to resolve any workspace whose Status is not Active, so suspension needs no new
        // column and no new enforcement anywhere.
        // ponytail: suspension and a (future) owner-initiated archive would share one status and be
        //  indistinguishable — split them only if owners ever get their own archive action.
        if (suspend)
        {
            workspace.Archive();
        }
        else
        {
            workspace.Restore();
        }

        audit.Write(
            suspend ? "host.workspace.suspended" : "host.workspace.restored",
            nameof(Workspace), workspace.Id, new { workspace.Name, workspace.Slug });

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return workspace.Status.ToString();
    }

    /// <summary>
    /// Binds the request scope to the workspace being acted on. Host requests carry no
    /// <c>X-Workspace</c>, so nothing has set this — and the write paths below need it: the DbContext
    /// re-stamps <c>app.current_workspace</c> before saving (<c>ReapplyWorkspaceSessionAsync</c>), and
    /// <c>WorkspaceDeletionService</c> requires the target to BE the ambient workspace because
    /// <c>workspace_self_delete</c> (0092) authorizes the DELETE through that same session variable.
    ///
    /// Not a privilege escalation: the role recorded here is only used to satisfy the context's shape.
    /// The host admin gains no Workspace membership, and every Workspace-scoped endpoint still resolves
    /// its own context from <c>WorkspaceResolutionMiddleware</c>, which requires real membership.
    /// </summary>
    private void EnterWorkspace(Guid workspaceId)
        => workspaceAccessor.Set(new WorkspaceContext(
            workspaceId,
            currentUser.UserId,
            membershipId: null,
            role: MembershipRole.Owner.ToString(),
            permissions: new HashSet<string>(),
            entitlements: new HashSet<string>(),
            correlationId: string.Empty));

    private async Task<Modules.Identity.Domain.User> RequireUserAsync(Guid userId, CancellationToken cancellationToken)
        => await users.FindByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");
}
