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

// ---- Commands ----
public sealed record CreateDocumentCommand(string Title, string Content, bool IsPrivate, Guid? SpaceId, Guid? ListId, Guid? TaskId, Guid? ParentDocumentId, Guid? TemplateId);

public sealed record UpdateDocumentCommand(string? Title, string? Content, bool? IsPrivate);
