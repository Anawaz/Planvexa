namespace Planvexa.Modules.Whiteboards.Application;

// ---- DTOs ----
public sealed record WhiteboardDto(
    Guid Id, string Name, bool IsPrivate, Guid OwnerUserId,
    string? LinkedResourceType, Guid? LinkedResourceId, bool IsArchived, DateTimeOffset UpdatedAtUtc);

/// <summary>Result of the internal collaboration-room authorization check (mirrors the
/// <c>CollaborationAccessDto</c> exactly) — the ONLY signal the Hocuspocus server's onAuthenticate hook
/// trusts before admitting a WebSocket connection into a whiteboard's room.</summary>
public sealed record WhiteboardCollaborationAccessDto(bool Allowed, bool CanEdit, Guid? UserId);

public sealed record WhiteboardTemplateDto(Guid Id, string Name, DateTimeOffset CreatedAtUtc);

// ---- Commands ----
public sealed record CreateWhiteboardCommand(string Name, bool IsPrivate, string? LinkedResourceType, Guid? LinkedResourceId, Guid? TemplateId);

public sealed record UpdateWhiteboardCommand(string? Name, bool? IsPrivate);
