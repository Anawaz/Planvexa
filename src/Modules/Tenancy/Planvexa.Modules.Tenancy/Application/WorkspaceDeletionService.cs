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
        var context = RequireWorkspace();
        if (context.WorkspaceId != workspaceId)
        {
            // RLS authorizes the DELETE through the ambient workspace GUC, so the target must BE the
            // ambient workspace — a caller cannot delete a workspace they are not currently inside.
            throw new ForbiddenException("A workspace can only be deleted from within itself.");
        }

        var workspace = await workspaces.FindByIdAsync(workspaceId, cancellationToken)
            ?? throw new NotFoundException("Workspace not found.");

        var caller = await memberships.FindAsync(workspace.Id, context.UserId, cancellationToken);
        if (caller?.Role != MembershipRole.Owner)
        {
            throw new ForbiddenException("Only an Owner can delete a workspace.");
        }

        if (!string.Equals(confirmSlug, workspace.Slug, StringComparison.Ordinal))
        {
            throw new ConflictException("The confirmation does not match this workspace's slug.");
        }

        // Written (and committed) BEFORE the delete: audit.audit_events is deliberately outside the
        // cascade (0092), so the record of the deletion outlives the workspace it describes.
        audit.Write("workspace.deleted", nameof(Workspace), workspace.Id, new { workspace.Name, workspace.Slug });
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

    private IWorkspaceContext RequireWorkspace()
    {
        var ctx = workspaceAccessor.Current;
        if (!ctx.HasWorkspace)
        {
            throw new ForbiddenException("A workspace context is required for this operation.");
        }

        return ctx;
    }
}
