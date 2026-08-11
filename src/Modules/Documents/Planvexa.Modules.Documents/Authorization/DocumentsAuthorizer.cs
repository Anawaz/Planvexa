namespace Planvexa.Modules.Documents.Authorization;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Documents.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Documents authorization. Workspace members may create and edit shared documents. Guests are
/// read-only. Private documents are visible to their owner and may be administered by Admin+ users.
/// </summary>
public static class DocumentsAuthorizer
{
    public static bool CanRead(WorkspaceRole? role) => role is not null;

    public static bool CanEdit(WorkspaceRole? role) => role >= WorkspaceRole.Member;

    public static bool CanManage(WorkspaceRole? role) => role >= WorkspaceRole.Admin;

    public static void EnsureRead(WorkspaceRole? role)
    {
        if (!CanRead(role))
        {
            throw new ForbiddenException("You do not have access to this workspace.");
        }
    }

    public static void EnsureEdit(WorkspaceRole? role)
    {
        EnsureRead(role);
        if (!CanEdit(role))
        {
            throw new ForbiddenException("Guests cannot modify documents in this workspace.");
        }
    }

    public static void EnsureManage(WorkspaceRole? role)
    {
        EnsureRead(role);
        if (!CanManage(role))
        {
            throw new ForbiddenException("Administrator access is required for this documents operation.");
        }
    }

    /// <summary>
    /// ADR-0003: a private document is visible to its owner and, in addition, to anyone explicitly
    /// granted access on it via <see cref="Application.Services.DocumentSharingService"/> (any grant
    /// level is enough to view — mirrors WorkManagementAuthorizer's read check, but simpler since a
    /// Document has no ACL-walkable ancestor, see DocumentResourceHierarchyQuery). A non-private document
    /// is visible to the whole workspace already, so the ACL is never consulted for it.
    /// </summary>
    public static async Task<bool> CanViewAsync(
        Document document, Guid userId, IResourcePermissionQuery acl, CancellationToken ct)
    {
        if (!document.IsPrivate || document.OwnerUserId == userId)
        {
            return true;
        }

        var level = await acl.GetEffectiveAsync(
            document.WorkspaceId, userId, DocumentResourceTypes.Document, document.Id, ct);
        return level is not null;
    }

    public static async Task EnsureViewableAsync(
        Document document, Guid userId, IResourcePermissionQuery acl, CancellationToken ct)
    {
        if (!await CanViewAsync(document, userId, acl, ct))
        {
            throw new ForbiddenException("This document is private to its owner.");
        }
    }

    /// <summary>Only relevant for private documents — the owner and workspace Admin+ may always
    /// modify; otherwise an explicit Edit-or-higher ACL grant is required.</summary>
    public static async Task<bool> CanModifyPrivateAsync(
        Document document, Guid userId, WorkspaceRole? role, IResourcePermissionQuery acl, CancellationToken ct)
    {
        if (!document.IsPrivate || document.OwnerUserId == userId || CanManage(role))
        {
            return true;
        }

        var level = await acl.GetEffectiveAsync(
            document.WorkspaceId, userId, DocumentResourceTypes.Document, document.Id, ct);
        return level is not null && level >= PermissionLevel.Edit;
    }

    public static async Task EnsureCanModifyPrivateAsync(
        Document document, Guid userId, WorkspaceRole? role, IResourcePermissionQuery acl, CancellationToken ct)
    {
        if (!await CanModifyPrivateAsync(document, userId, role, acl, ct))
        {
            throw new ForbiddenException("Only the document owner or a workspace administrator can edit this private document.");
        }
    }
}
