namespace Planvexa.Modules.Documents.Application;

// ---- DTOs ----
public sealed record DocumentDto(Guid Id, string Title, string Content, bool IsPrivate, Guid OwnerUserId, Guid? SpaceId, Guid? ListId, Guid? TaskId, Guid? ParentDocumentId, DateTimeOffset UpdatedAtUtc);

public sealed record DocumentSummaryDto(Guid Id, string Title, bool IsPrivate, Guid OwnerUserId, Guid? SpaceId, Guid? ListId, Guid? TaskId, Guid? ParentDocumentId, DateTimeOffset UpdatedAtUtc);

public sealed record DocumentVersionDto(Guid Id, Guid AuthorUserId, DateTimeOffset CreatedAtUtc, string ContentPreview);

/// <summary>Result of the internal collaboration-room authorization check  — the ONLY
/// signal the Hocuspocus server's onAuthenticate hook trusts before admitting a WebSocket connection into
/// a document's room. <see cref="CanEdit"/> lets the room mark a read-only participant.</summary>
public sealed record CollaborationAccessDto(bool Allowed, bool CanEdit, Guid? UserId);

public sealed record DocumentTemplateDto(Guid Id, string Name, DateTimeOffset CreatedAtUtc);

public sealed record DocumentCommentDto(Guid Id, Guid AuthorUserId, string Body, DateTimeOffset CreatedAtUtc);

public sealed record DocumentShareLinkDto(Guid Id, Guid DocumentId, string Token, string Url, DateTimeOffset? ExpiresAtUtc, bool RequiresPassword);

/// <summary>Anonymous projection returned by the public read path — the document's title and rendered
/// Markdown content (via LexicalMarkdown, same renderer as the authenticated export endpoint), never
/// the raw Lexical JSON, comments, versions, or any other workspace data.</summary>
public sealed record SharedDocumentDto(Guid DocumentId, string Title, string ContentMarkdown, DateTimeOffset UpdatedAtUtc);

/// <summary>Outcome of an anonymous public document share-link lookup, distinguishing "no such link"
/// from "wrong/missing password" — mirrors Collaboration's ShareLinkAccessStatus for tasks.</summary>
public enum DocumentShareAccessStatus
{
    NotFound,
    PasswordRequired,
    InvalidPassword,
    Ok,
}

public sealed record SharedDocumentAccessResult(DocumentShareAccessStatus Status, SharedDocumentDto? Document)
{
    public static readonly SharedDocumentAccessResult NotFound = new(DocumentShareAccessStatus.NotFound, null);
    public static readonly SharedDocumentAccessResult PasswordRequired = new(DocumentShareAccessStatus.PasswordRequired, null);
    public static readonly SharedDocumentAccessResult InvalidPassword = new(DocumentShareAccessStatus.InvalidPassword, null);
}

// ---- Commands ----
public sealed record CreateDocumentCommand(string Title, string Content, bool IsPrivate, Guid? SpaceId, Guid? ListId, Guid? TaskId, Guid? ParentDocumentId, Guid? TemplateId);

public sealed record UpdateDocumentCommand(string? Title, string? Content, bool? IsPrivate);
