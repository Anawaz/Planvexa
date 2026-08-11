namespace Planvexa.Modules.Clips.Application.Services;

using Planvexa.Modules.Clips.Authorization;
using Planvexa.Modules.Clips.Domain;

/// <summary>Clip comments — a flat, timestamped list gated by the exact same
/// access rule as the clip itself (see ClipService.CanAccessAsync's doc comment).</summary>
public sealed class ClipCommentService(ClipServiceContext ctx, IClipCommentStore comments, ClipService clipService)
    : ClipServiceBase(ctx)
{
    public async Task<IReadOnlyList<ClipCommentDto>> ListAsync(Guid clipId, CancellationToken ct)
    {
        var (clip, _) = await clipService.LoadForReadAsync(clipId, ct);
        var list = await comments.ListByClipAsync(clip.WorkspaceId, clip.Id, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<ClipCommentDto> AddAsync(Guid clipId, string body, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var (clip, role) = await clipService.LoadForReadAsync(clipId, ct);
        ClipsAuthorizer.EnsureEdit(role);

        var comment = ClipComment.Create(NewId(), workspaceId, clip.Id, UserId, body, Now);
        comments.Add(comment);
        Audit("clips.comment_added", "Clip", clip.Id);
        await SaveAsync(ct);
        return ToDto(comment);
    }

    private static ClipCommentDto ToDto(ClipComment c) => new(c.Id, c.AuthorUserId, c.Body, c.CreatedAtUtc);
}
