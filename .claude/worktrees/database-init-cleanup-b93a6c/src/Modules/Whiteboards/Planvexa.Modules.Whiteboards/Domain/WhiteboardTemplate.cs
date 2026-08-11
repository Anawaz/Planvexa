namespace Planvexa.Modules.Whiteboards.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// A reusable whiteboard content snapshot (mirrors <c>DocumentTemplate</c>'s doc comment
/// for why this isn't WorkTemplate: it captures CONTENT, not workspace structure). Unlike
/// DocumentTemplate (whose content is plain Lexical JSON text living directly on the Document entity),
/// a whiteboard's content is Yjs binary CRDT state that only ever lives in
/// <c>whiteboards.whiteboard_collab_state</c> (owned by apps/collaboration, see Whiteboard's doc comment)
/// — so <see cref="SeedState"/> is a raw byte-for-byte copy of that row's <c>y_state</c>, taken at
/// "create template from whiteboard" time (a snapshot, not live — the same "resumable working buffer, not
/// durable history" caveat documented on document_collab_state applies: if the source whiteboard has
/// unflushed in-memory edits in an active collaboration room, the snapshot is only as fresh as the last
/// periodic flush). Applying a template seeds a brand-new whiteboard's collab-state row with these bytes
/// before its first room is ever opened.
/// </summary>
public sealed class WhiteboardTemplate : Entity, IWorkspaceOwned
{
    private WhiteboardTemplate()
    {
    }

    private WhiteboardTemplate(Guid id, Guid workspaceId, string name, byte[]? seedState, Guid createdByUserId, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        SeedState = seedState;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>Null when the source whiteboard had no saved collaboration state yet (a brand-new, never
    /// opened whiteboard) — applying such a template just creates a blank whiteboard.</summary>
    public byte[]? SeedState { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static WhiteboardTemplate Create(Guid id, Guid workspaceId, string name, byte[]? seedState, Guid createdByUserId, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstEmpty(createdByUserId, nameof(createdByUserId));
        return new WhiteboardTemplate(id, workspaceId, name.Trim(), seedState, createdByUserId, nowUtc);
    }
}
