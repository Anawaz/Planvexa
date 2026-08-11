namespace Planvexa.Modules.Clips.Application;

using Planvexa.Modules.Clips.Domain;

// ---- DTOs ----
public sealed record ClipDto(
    Guid Id, string Title, string? Description, bool IsPrivate, Guid OwnerUserId,
    string? LinkedResourceType, Guid? LinkedResourceId,
    string ContentType, long SizeBytes, double? DurationSeconds, ClipStatus Status, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record ClipCommentDto(Guid Id, Guid AuthorUserId, string Body, DateTimeOffset CreatedAtUtc);

public sealed record ClipTranscriptSegmentDto(double StartSeconds, double EndSeconds, string Text);

public sealed record ClipTranscriptDto(ClipTranscriptStatus Status, string? Text, IReadOnlyList<ClipTranscriptSegmentDto>? Segments, DateTimeOffset UpdatedAtUtc);

// ---- Commands ----
public sealed record UpdateClipCommand(string? Title, string? Description, bool? IsPrivate);
