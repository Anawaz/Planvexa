namespace Planvexa.Modules.Tenancy.Application;

using Microsoft.Extensions.Logging;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Tenancy.Domain;

/// <summary>
/// Permanently deletes a Workspace and everything inside it. Irreversible: there is no archive or
/// restore path, so the caller must be an Owner AND retype the workspace slug. The row deletion is a
/// single DELETE — PostgreSQL cascades every workspace-owned table from it (script 0092) — and the
/// blob subtree is swept afterwards.
/// </summary>
public sealed class WorkspaceDeletionService(
    IWorkspaceContextAccessor workspaceAccessor,
    IWorkspaceStore workspaces,
    IMembershipStore memberships,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    IFileStorage fileStorage,
    ILogger<WorkspaceDeletionService> logger)
{
    public async Task DeleteAsync(Guid workspaceId, string confirmSlug, CancellationToken cancellationToken = default)
    {
        var context = RequireTargetWorkspace(workspaceId);

        var workspace = await workspaces.FindByIdAsync(workspaceId, cancellationToken)
            ?? throw new NotFoundException("Workspace not found.");

        var caller = await memberships.FindAsync(workspace.Id, context.UserId, cancellationToken);
        if (caller?.Role != MembershipRole.Owner)
        {
            throw new ForbiddenException("Only an Owner can delete a workspace.");
        }

        await DeleteCoreAsync(workspace, confirmSlug, "workspace.deleted", cancellationToken);
    }

    /// <summary>
    /// The host-administrator deletion path: identical to <see cref="DeleteAsync"/> — same cascade,
    /// same blob sweep, same retyped-slug confirmation — minus the Owner-membership check, because a
    /// host administrator administers the installation and is deliberately not a member of the
    /// Workspaces in it.
    ///
    /// This method does NOT authorize anything itself; whoever calls it must already have established
    /// host-administrator status. Today that is the <c>HostAdmin</c> endpoint policy on
    /// <c>/api/v1/host/*</c>, backed by the host-admin RLS policies in script 0094.
    /// </summary>
    public async Task DeleteAsHostAdminAsync(Guid workspaceId, string confirmSlug, CancellationToken cancellationToken = default)
    {
        RequireTargetWorkspace(workspaceId);

        var workspace = await workspaces.FindByIdAsync(workspaceId, cancellationToken)
            ?? throw new NotFoundException("Workspace not found.");

        await DeleteCoreAsync(workspace, confirmSlug, "host.workspace.deleted", cancellationToken);
    }

    private async Task DeleteCoreAsync(
        Domain.Workspace workspace, string confirmSlug, string auditAction, CancellationToken cancellationToken)
    {
        var workspaceId = workspace.Id;

        if (!string.Equals(confirmSlug, workspace.Slug, StringComparison.Ordinal))
        {
            throw new ConflictException("The confirmation does not match this workspace's slug.");
        }

        // Written (and committed) BEFORE the delete: audit.audit_events is deliberately outside the
        // cascade (0092), so the record of the deletion outlives the workspace it describes.
        audit.Write(auditAction, nameof(Workspace), workspace.Id, new { workspace.Name, workspace.Slug });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await workspaces.DeleteCascadeAsync(workspaceId, cancellationToken);

        // Blobs live outside the transaction, so this is best-effort: the rows are already gone and
        // re-running the delete is not possible. Orphaned bytes are recoverable; a failed request that
        // has already deleted the database rows is not.
        try
        {
            await fileStorage.DeletePrefixAsync($"workspaces/{workspaceId}/", cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Workspace {WorkspaceId} was deleted but its stored files could not be removed.", workspaceId);
        }
    }

    private IWorkspaceContext RequireTargetWorkspace(Guid workspaceId)
    {
        var ctx = workspaceAccessor.Current;
        if (!ctx.HasWorkspace)
        {
            throw new ForbiddenException("A workspace context is required for this operation.");
        }

        if (ctx.WorkspaceId != workspaceId)
        {
            // RLS authorizes the DELETE through the ambient workspace GUC (workspace_self_delete, 0092),
            // so the target must BE the ambient workspace — a caller cannot delete a workspace they are
            // not currently inside. The host-admin path satisfies this by binding the scope to the
            // target workspace before calling in (HostAdminActionService.EnterWorkspace).
            throw new ForbiddenException("A workspace can only be deleted from within itself.");
        }

        return ctx;
    }
}
