namespace Planvexa.Modules.Whiteboards.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// Resource-type strings a Whiteboard can link to — re-exports
/// <see cref="Planvexa.SharedContracts.Workspaces.LinkedResourceTypes"/> so callers inside this module
/// don't need the SharedContracts namespace open everywhere.
/// </summary>
public static class WhiteboardLinkedResourceTypes
{
    public const string Task = Planvexa.SharedContracts.Workspaces.LinkedResourceTypes.Task;
    public const string Document = Planvexa.SharedContracts.Workspaces.LinkedResourceTypes.Document;
}

/// <summary>
/// A workspace whiteboard. Shapes/connectors/sticky-notes/text/images are Yjs CRDT
/// state persisted by apps/collaboration's Hocuspocus server into <c>whiteboards.whiteboard_collab_state</c>
/// (same pattern as Documents' <c>document_collab_state</c>) — this aggregate only holds
/// metadata + the privacy/linking rule, mirroring <c>Document</c>'s "private to owner" model plus
/// <c>ChatChannel</c>'s "linked resource inherits the linked resource's ACL" model, combined: a plain
/// Whiteboard is private-to-owner or workspace-visible (<see cref="CanBeViewedBy"/>, sync/structural,
/// exactly like <c>Document.CanBeViewedBy</c>); a LINKED whiteboard (<see cref="LinkedResourceType"/> set)
/// is never itself private — visibility instead comes from the linked Task/Document's own ACL, resolved
/// asynchronously by WhiteboardService (mirrors ChatChannelService.CanAccessAsync, which needs the same
/// two-layer split for the identical reason: the async cross-module ACL walk cannot live on a pure
/// domain entity).
/// </summary>
public sealed class Whiteboard : Entity, IAggregateRoot, IWorkspaceOwned
{
    private Whiteboard()
    {
    }

    private Whiteboard(
        Guid id, Guid workspaceId, string name, bool isPrivate, Guid ownerUserId,
        string? linkedResourceType, Guid? linkedResourceId, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        IsPrivate = isPrivate;
        OwnerUserId = ownerUserId;
        LinkedResourceType = linkedResourceType;
        LinkedResourceId = linkedResourceId;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsPrivate { get; private set; }
    public Guid OwnerUserId { get; private set; }

    /// <summary>Set together with <see cref="LinkedResourceId"/>; one of <see cref="WhiteboardLinkedResourceTypes"/>.</summary>
    public string? LinkedResourceType { get; private set; }
    public Guid? LinkedResourceId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public bool IsArchived => ArchivedAtUtc is not null;

    public static Whiteboard Create(Guid id, Guid workspaceId, string name, bool isPrivate, Guid ownerUserId, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstEmpty(ownerUserId, nameof(ownerUserId));
        return new Whiteboard(id, workspaceId, name.Trim(), isPrivate, ownerUserId, null, null, nowUtc);
    }

    /// <summary>Creates a whiteboard linked to a Task/Document. Never private by itself — see class doc
    /// comment; visibility is gated by the linked resource's own ACL, resolved by WhiteboardService.</summary>
    public static Whiteboard CreateLinked(
        Guid id, Guid workspaceId, string name, string linkedResourceType, Guid linkedResourceId, Guid ownerUserId, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstNullOrWhiteSpace(linkedResourceType, nameof(linkedResourceType));
        Guard.AgainstEmpty(linkedResourceId, nameof(linkedResourceId));
        Guard.AgainstEmpty(ownerUserId, nameof(ownerUserId));

        if (linkedResourceType is not (WhiteboardLinkedResourceTypes.Task or WhiteboardLinkedResourceTypes.Document))
        {
            throw new ValidationAppException("linkedResourceType must be task or document.");
        }

        return new Whiteboard(id, workspaceId, name.Trim(), isPrivate: false, ownerUserId, linkedResourceType, linkedResourceId, nowUtc);
    }

    public void UpdateDetails(string? name, bool? isPrivate, DateTimeOffset nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name.Trim();
        }

        if (isPrivate is { } value && LinkedResourceType is null)
        {
            // A linked whiteboard's visibility is never toggled directly — it always tracks the linked
            // resource (see class doc comment); silently ignore rather than throw, so a generic "update"
            // request that happens to include isPrivate never fails for a linked whiteboard.
            IsPrivate = value;
        }

        UpdatedAtUtc = nowUtc;
    }

    public void Archive(DateTimeOffset nowUtc) => ArchivedAtUtc ??= nowUtc;

    /// <summary>
    /// Structural (synchronous) visibility check — the exact same "private to owner, else any workspace
    /// member" rule as <c>Document.CanBeViewedBy</c> (the workspace-member floor itself is enforced by the
    /// caller via <c>WhiteboardsAuthorizer.EnsureRead(role)</c> before this ever runs, same division of
    /// labor DocumentService uses). For a LINKED whiteboard this is only half the story: WhiteboardService
    /// ANDs it with the linked resource's async ACL check (<see cref="LinkedResourceType"/> is never
    /// itself private, so this half always passes for a linked whiteboard — the linked-resource check is
    /// what actually gates it).
    /// </summary>
    public bool CanBeViewedBy(Guid userId) => !IsPrivate || OwnerUserId == userId;

    public void EnsureViewableBy(Guid userId)
    {
        if (!CanBeViewedBy(userId))
        {
            throw new ForbiddenException("This whiteboard is private to its owner.");
        }
    }
}
