namespace Planvexa.Modules.Clips.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// A lightweight, clip-scoped comment thread (a deliberate design choice — same call Goals made for
/// <c>GoalComment</c>, and for the same reason: Clips cannot reference the Collaboration module directly
/// (AGENTS.md rule 7), and wiring a full cross-module contract for "post a comment" plus reusing
/// Collaboration's mention/reaction/share-link machinery would be disproportionate to what a clip's
/// comment thread needs — a flat list of timestamped remarks, no threading/reactions/mentions).
/// </summary>
public sealed class ClipComment : Entity, IWorkspaceOwned
{
    private ClipComment()
    {
    }

    private ClipComment(Guid id, Guid workspaceId, Guid clipId, Guid authorUserId, string body, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        ClipId = clipId;
        AuthorUserId = authorUserId;
        Body = body;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid ClipId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static ClipComment Create(Guid id, Guid workspaceId, Guid clipId, Guid authorUserId, string body, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(body, nameof(body));
        Guard.AgainstEmpty(authorUserId, nameof(authorUserId));
        return new ClipComment(id, workspaceId, clipId, authorUserId, body.Trim(), nowUtc);
    }
}
