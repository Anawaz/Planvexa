namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Clips.Application;
using Planvexa.Modules.Clips.Domain;

internal sealed class ClipStore(PlanvexaDbContext db) : IClipStore
{
    public void Add(Clip clip) => db.Set<Clip>().Add(clip);

    public void Remove(Clip clip) => db.Set<Clip>().Remove(clip);

    public Task<Clip?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<Clip>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Clip>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<Clip>()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
}

internal sealed class ClipCommentStore(PlanvexaDbContext db) : IClipCommentStore
{
    public void Add(ClipComment comment) => db.Set<ClipComment>().Add(comment);

    public async Task<IReadOnlyList<ClipComment>> ListByClipAsync(Guid workspaceId, Guid clipId, CancellationToken ct = default)
        => await db.Set<ClipComment>()
            .Where(x => x.WorkspaceId == workspaceId && x.ClipId == clipId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);
}

internal sealed class ClipTranscriptStore(PlanvexaDbContext db) : IClipTranscriptStore
{
    public void Add(ClipTranscript transcript) => db.Set<ClipTranscript>().Add(transcript);

    public void Remove(ClipTranscript transcript) => db.Set<ClipTranscript>().Remove(transcript);

    public Task<ClipTranscript?> FindByClipAsync(Guid workspaceId, Guid clipId, CancellationToken ct = default)
        => db.Set<ClipTranscript>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.ClipId == clipId, ct);

    public async Task<IReadOnlyDictionary<Guid, ClipTranscript>> ListReadyByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => (await db.Set<ClipTranscript>()
            .Where(x => x.WorkspaceId == workspaceId && x.Status == ClipTranscriptStatus.Ready)
            .ToListAsync(ct))
            .ToDictionary(x => x.ClipId);
}
