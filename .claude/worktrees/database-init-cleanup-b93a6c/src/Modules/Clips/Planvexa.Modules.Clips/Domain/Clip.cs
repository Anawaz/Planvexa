namespace Planvexa.Modules.Clips.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>Resource-type strings a Clip can link to — see WhiteboardLinkedResourceTypes' doc comment;
/// the same rationale applies (re-exports the SharedContracts constants for a local, module-scoped name).</summary>
public static class ClipLinkedResourceTypes
{
    public const string Task = Planvexa.SharedContracts.Workspaces.LinkedResourceTypes.Task;
    public const string Document = Planvexa.SharedContracts.Workspaces.LinkedResourceTypes.Document;
}

/// <summary>
/// Lifecycle of a Clip's media file. Recording happens entirely client-side (browser MediaRecorder API,
/// Item 7) — by the time a Clip row exists server-side the bytes are already fully uploaded, so
/// <see cref="Recording"/>/<see cref="Processing"/> are not currently reachable through the upload
/// endpoint (no server-side transcoding pipeline exists here); they exist on the enum for the
/// schema to support a future async processing step (e.g. thumbnail/waveform generation) without another
/// migration.
/// </summary>
public enum ClipStatus
{
    Recording = 0,
    Processing = 1,
    Ready = 2,
    Failed = 3,
}

/// <summary>
/// A workspace clip: an uploaded (or browser-recorded-then-uploaded) video/audio file (net
/// new). Privacy/linking model is a direct copy of <c>Whiteboard</c>'s (itself modeled on
/// <c>Document</c>'s owner-private + <c>ChatChannel</c>'s linked-resource-inherits-ACL patterns) — see
/// Whiteboard's class doc comment for the full rationale; duplicated here rather than shared because the
/// two modules must not reference each other (AGENTS.md rule 7).
/// </summary>
public sealed class Clip : Entity, IAggregateRoot, IWorkspaceOwned
{
    private Clip()
    {
    }

    private Clip(
        Guid id, Guid workspaceId, string title, string? description, bool isPrivate, Guid ownerUserId,
        string? linkedResourceType, Guid? linkedResourceId,
        string storagePath, string contentType, long sizeBytes, double? durationSeconds, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Title = title;
        Description = description;
        IsPrivate = isPrivate;
        OwnerUserId = ownerUserId;
        LinkedResourceType = linkedResourceType;
        LinkedResourceId = linkedResourceId;
        StoragePath = storagePath;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        DurationSeconds = durationSeconds;
        Status = ClipStatus.Ready;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsPrivate { get; private set; }
    public Guid OwnerUserId { get; private set; }

    /// <summary>Set together with <see cref="LinkedResourceId"/>; one of <see cref="ClipLinkedResourceTypes"/>.</summary>
    public string? LinkedResourceType { get; private set; }
    public Guid? LinkedResourceId { get; private set; }

    public string StoragePath { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public double? DurationSeconds { get; private set; }
    public ClipStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Clip Create(
        Guid id, Guid workspaceId, string title, string? description, bool isPrivate, Guid ownerUserId,
        string storagePath, string contentType, long sizeBytes, double? durationSeconds, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(title, nameof(title));
        Guard.AgainstNullOrWhiteSpace(storagePath, nameof(storagePath));
        Guard.AgainstEmpty(ownerUserId, nameof(ownerUserId));
        return new Clip(id, workspaceId, title.Trim(), Normalize(description), isPrivate, ownerUserId, null, null, storagePath, contentType, sizeBytes, durationSeconds, nowUtc);
    }

    /// <summary>Creates a clip linked to a Task/Document. Never private by itself — see class doc comment.</summary>
    public static Clip CreateLinked(
        Guid id, Guid workspaceId, string title, string? description, string linkedResourceType, Guid linkedResourceId, Guid ownerUserId,
        string storagePath, string contentType, long sizeBytes, double? durationSeconds, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(title, nameof(title));
        Guard.AgainstNullOrWhiteSpace(storagePath, nameof(storagePath));
        Guard.AgainstNullOrWhiteSpace(linkedResourceType, nameof(linkedResourceType));
        Guard.AgainstEmpty(linkedResourceId, nameof(linkedResourceId));
        Guard.AgainstEmpty(ownerUserId, nameof(ownerUserId));

        if (linkedResourceType is not (ClipLinkedResourceTypes.Task or ClipLinkedResourceTypes.Document))
        {
            throw new ValidationAppException("linkedResourceType must be task or document.");
        }

        return new Clip(id, workspaceId, title.Trim(), Normalize(description), isPrivate: false, ownerUserId, linkedResourceType, linkedResourceId, storagePath, contentType, sizeBytes, durationSeconds, nowUtc);
    }

    public void UpdateDetails(string? title, string? description, bool? isPrivate, DateTimeOffset nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title.Trim();
        }

        if (description is not null)
        {
            Description = Normalize(description);
        }

        if (isPrivate is { } value && LinkedResourceType is null)
        {
            IsPrivate = value;
        }

        UpdatedAtUtc = nowUtc;
    }

    public void MarkFailed(DateTimeOffset nowUtc)
    {
        Status = ClipStatus.Failed;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Structural (synchronous) visibility check — identical rule to <c>Whiteboard.CanBeViewedBy</c>/
    /// <c>Document.CanBeViewedBy</c>.</summary>
    public bool CanBeViewedBy(Guid userId) => !IsPrivate || OwnerUserId == userId;

    public void EnsureViewableBy(Guid userId)
    {
        if (!CanBeViewedBy(userId))
        {
            throw new ForbiddenException("This clip is private to its owner.");
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
