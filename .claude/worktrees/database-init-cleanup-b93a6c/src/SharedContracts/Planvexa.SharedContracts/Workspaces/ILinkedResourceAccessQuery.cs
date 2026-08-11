namespace Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Free-form resource-type strings a Whiteboard/Clip may link to. Deliberately not reusing
/// Chat's <c>ChatLinkedResourceTypes</c> (Space/List/Task) — Whiteboards/Clips link to a Task OR a
/// Document, and Chat's constants live inside the Chat module, which neither module may reference
/// (AGENTS.md rule 7). Shared here so both new modules use the exact same strings.
/// </summary>
public static class LinkedResourceTypes
{
    public const string Task = "task";
    public const string Document = "document";
}

/// <summary>
/// Cross-module contract (implemented in Infrastructure) that answers "can this user view resource X",
/// for the two linkable resource kinds a Whiteboard/Clip may attach to (linked-resource
/// privacy inheritance, the same pattern Chat channels use for Space/List/Task links — see
/// ChatChannelService.CanAccessAsync). WorkManagement's Task ACL is resolved via the existing
/// <see cref="IResourcePermissionQuery"/> resolver; Documents has no ACL system of its own (just
/// <c>IsPrivate</c>/owner), so that branch re-runs <c>Document.CanBeViewedBy</c> directly — mirroring how
/// <c>IAiDocumentContentSource</c> already bridges the same gap for AI content sources. A Whiteboard/Clip linked to
/// a private Task/Document must be exactly as hidden as that Task/Document.
/// </summary>
public interface ILinkedResourceAccessQuery
{
    Task<bool> CanViewAsync(Guid workspaceId, Guid userId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default);
}
