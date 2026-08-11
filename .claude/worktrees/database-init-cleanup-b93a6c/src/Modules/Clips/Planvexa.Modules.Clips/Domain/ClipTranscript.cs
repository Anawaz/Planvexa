namespace Planvexa.Modules.Clips.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>Lifecycle of a Clip's transcript.</summary>
public enum ClipTranscriptStatus
{
    /// <summary>No transcription-capable AI provider is configured/enabled for this workspace — a
    /// documented, honest gap, never a faked transcript (see IClipTranscriber's doc comment).</summary>
    Unavailable = 0,
    Pending = 1,
    Ready = 2,
    Failed = 3,
}

/// <summary>
/// A clip's transcript: full text plus optional per-segment timestamps ("searchable
/// transcripts" — indexed into the cross-module search fan-out by <c>ClipSearchProvider</c>, permission-
/// filtered identically to the clip itself, never separately). One per Clip (enforced by a unique index on
/// <c>ClipId</c>) — a re-requested transcription overwrites rather than accumulates rows, since only the
/// latest transcript is ever useful.
/// </summary>
public sealed class ClipTranscript : Entity, IWorkspaceOwned
{
    private ClipTranscript()
    {
    }

    private ClipTranscript(Guid id, Guid workspaceId, Guid clipId, ClipTranscriptStatus status, string? text, string? segmentsJson, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        ClipId = clipId;
        Status = status;
        Text = text;
        SegmentsJson = segmentsJson;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid ClipId { get; private set; }
    public ClipTranscriptStatus Status { get; private set; }
    public string? Text { get; private set; }

    /// <summary>Raw JSON array of <c>{startSeconds,endSeconds,text}</c> segments, parsed by the application
    /// layer — same "opaque JSON text on the entity" convention as SavedView.ConfigJson/AiProviderSettings'
    /// list columns. Null when the provider only returned plain text (see IClipTranscriber's doc comment).</summary>
    public string? SegmentsJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static ClipTranscript CreateUnavailable(Guid id, Guid workspaceId, Guid clipId, DateTimeOffset nowUtc)
        => new(id, workspaceId, clipId, ClipTranscriptStatus.Unavailable, null, null, nowUtc);

    public static ClipTranscript CreatePending(Guid id, Guid workspaceId, Guid clipId, DateTimeOffset nowUtc)
        => new(id, workspaceId, clipId, ClipTranscriptStatus.Pending, null, null, nowUtc);

    public void MarkReady(string text, string? segmentsJson, DateTimeOffset nowUtc)
    {
        Status = ClipTranscriptStatus.Ready;
        Text = text;
        SegmentsJson = segmentsJson;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkFailed(DateTimeOffset nowUtc)
    {
        Status = ClipTranscriptStatus.Failed;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkUnavailable(DateTimeOffset nowUtc)
    {
        Status = ClipTranscriptStatus.Unavailable;
        UpdatedAtUtc = nowUtc;
    }
}
