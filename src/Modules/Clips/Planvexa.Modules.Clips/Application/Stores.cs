namespace Planvexa.Modules.Clips.Application;

using Planvexa.Modules.Clips.Domain;

public interface IClipStore
{
    void Add(Clip clip);
    void Remove(Clip clip);
    Task<Clip?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Clip>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface IClipCommentStore
{
    void Add(ClipComment comment);
    Task<IReadOnlyList<ClipComment>> ListByClipAsync(Guid workspaceId, Guid clipId, CancellationToken ct = default);
}

public interface IClipTranscriptStore
{
    void Add(ClipTranscript transcript);
    void Remove(ClipTranscript transcript);
    Task<ClipTranscript?> FindByClipAsync(Guid workspaceId, Guid clipId, CancellationToken ct = default);

    /// <summary>All Ready transcripts in the workspace, keyed by ClipId — used by ClipSearchProvider to
    /// match transcript text without an N+1 query per candidate clip.</summary>
    Task<IReadOnlyDictionary<Guid, ClipTranscript>> ListReadyByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}
