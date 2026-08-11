namespace Planvexa.Modules.Documents.Application.Services;

using Planvexa.Modules.Documents.Authorization;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Implements the cross-module <see cref="IResourceHierarchyQuery"/> for Documents' single ACL
/// resource type (ADR-0003), so Tenancy's resolver can evaluate a Document's own privacy/ACL grants
/// without reading this module's tables directly (AGENTS.md rule 7). A Document has no ACL-walkable
/// parent of its own — ParentDocumentId is a wiki-nesting concern (<see cref="Domain.DocumentHierarchy"/>),
/// not a sharing/inheritance one — so this always reports a top-level node.
/// </summary>
public sealed class DocumentResourceHierarchyQuery(IDocumentStore docs) : IResourceHierarchyQuery
{
    public async Task<ResourceHierarchyNode?> GetAsync(
        string resourceType, Guid resourceId, CancellationToken cancellationToken = default)
    {
        if (resourceType != DocumentResourceTypes.Document)
        {
            return null;
        }

        var document = await docs.FindAsync(resourceId, cancellationToken);
        return document is null
            ? null
            : new ResourceHierarchyNode(document.WorkspaceId, document.IsPrivate, null, null, document.OwnerUserId);
    }
}
