namespace Planvexa.Modules.Documents.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Documents.Authorization;
using Planvexa.Modules.Documents.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// ADR-0003 private sharing for Documents: grants/revokes/lists tenancy.resource_permissions rows for
/// resourceType "document", mirroring WorkManagement's ResourceSharingService. Only the document's owner
/// or a workspace Admin+ may manage sharing — narrower than WorkManagement's Manage-or-Share rule since
/// Documents has no dedicated "share" grant level of its own (a Document's only levers are View/Edit —
/// see DocumentsAuthorizer.CanModifyPrivateAsync).
/// </summary>
public sealed class DocumentSharingService(DocumentsServiceContext ctx, IDocumentStore docs, IResourcePermissionAdmin aclAdmin)
    : DocumentsServiceBase(ctx)
{
    public async Task<IReadOnlyList<ResourcePermissionGrant>> ListAsync(Guid documentId, CancellationToken ct)
    {
        var document = await LoadAsync(documentId, ct);
        await EnsureCanShareAsync(document, ct);
        return await aclAdmin.ListForResourceAsync(document.WorkspaceId, DocumentResourceTypes.Document, documentId, ct);
    }

    public async Task<ResourcePermissionGrant> GrantAsync(
        Guid documentId, string principalType, Guid principalId, PermissionLevel level, CancellationToken ct)
    {
        var document = await LoadAsync(documentId, ct);
        await EnsureCanShareAsync(document, ct);

        var grant = await aclAdmin.GrantAsync(
            document.WorkspaceId, UserId, DocumentResourceTypes.Document, documentId, principalType, principalId, level, ct);
        Audit("docs.document.shared", "Document", documentId, new { principalType, principalId, level });
        await SaveAsync(ct);
        return grant;
    }

    public async Task RevokeAsync(Guid documentId, string principalType, Guid principalId, CancellationToken ct)
    {
        var document = await LoadAsync(documentId, ct);
        await EnsureCanShareAsync(document, ct);

        await aclAdmin.RevokeAsync(document.WorkspaceId, DocumentResourceTypes.Document, documentId, principalType, principalId, ct);
        Audit("docs.document.unshared", "Document", documentId, new { principalType, principalId });
        await SaveAsync(ct);
    }

    private async Task<Document> LoadAsync(Guid documentId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var document = await docs.FindAsync(documentId, ct)
            ?? throw new NotFoundException("Document not found.");
        if (document.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Document not found.");
        }

        return document;
    }

    /// <summary>Only the document's owner or a workspace Admin+ may grant/revoke/list its sharing
    /// grants — same rule as <see cref="DocumentService"/>'s delete/EnsureOwnerOrAdmin check.</summary>
    private async Task EnsureCanShareAsync(Document document, CancellationToken ct)
    {
        var role = (await AccessAsync(document.WorkspaceId, ct))?.Role;
        DocumentsAuthorizer.EnsureRead(role);
        if (document.OwnerUserId != UserId && !DocumentsAuthorizer.CanManage(role))
        {
            throw new ForbiddenException("Only the document owner or a workspace administrator can manage sharing for this document.");
        }
    }
}
