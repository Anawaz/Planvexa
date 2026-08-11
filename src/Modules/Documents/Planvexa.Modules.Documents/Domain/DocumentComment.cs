namespace Planvexa.Modules.Documents.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// A lightweight, document-scoped comment thread — same deliberate design choice as
/// <c>ClipComment</c>/<c>GoalComment</c> (Documents cannot reference the Collaboration module directly,
/// AGENTS.md rule 7, and wiring a full cross-module contract for "post a comment" plus reusing
/// Collaboration's mention/reaction/threading machinery would be disproportionate here): a flat list of
/// timestamped remarks, no threading/reactions/mentions. Visibility inherits entirely from the owning
/// Document (a private document's comments are exactly as hidden as the document itself — see
/// DocumentCommentService).
/// </summary>
public sealed class DocumentComment : Entity, IWorkspaceOwned
{
    private DocumentComment()
    {
    }

    private DocumentComment(Guid id, Guid workspaceId, Guid documentId, Guid authorUserId, string body, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        DocumentId = documentId;
        AuthorUserId = authorUserId;
        Body = body;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static DocumentComment Create(Guid id, Guid workspaceId, Guid documentId, Guid authorUserId, string body, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(body, nameof(body));
        Guard.AgainstEmpty(authorUserId, nameof(authorUserId));
        return new DocumentComment(id, workspaceId, documentId, authorUserId, body.Trim(), nowUtc);
    }
}
