namespace Planvexa.Modules.TimeTracking.Application.Services;

using Planvexa.Modules.TimeTracking.Authorization;
using Planvexa.Modules.TimeTracking.Domain;

/// <summary>CRUD for the workspace's time-entry tag list. Mirrors WorkManagement's TagService.</summary>
public sealed class TimeTagService(TimeServiceContext ctx, ITimeTagStore tags) : TimeServiceBase(ctx)
{
    public async Task<IReadOnlyList<TimeTagDto>> ListAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureTrackOwn((await AccessAsync(workspaceId, ct))?.Role);
        var list = await tags.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(TimeMapper.ToDto).ToList();
    }

    /// <summary>Creates a tag, or returns the existing one for that name (case-insensitive) -- idempotent by design.</summary>
    public async Task<TimeTagDto> CreateAsync(CreateTimeTagCommand command, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureTrackOwn((await AccessAsync(workspaceId, ct))?.Role);

        var existing = await tags.FindByNameAsync(workspaceId, command.Name, ct);
        if (existing is not null)
        {
            return TimeMapper.ToDto(existing);
        }

        var tag = TimeTag.Create(NewId(), workspaceId, command.Name, Now);
        tags.Add(tag);
        Audit("time.tag_created", "TimeTag", tag.Id, new { name = tag.Name });
        await SaveAsync(ct);
        return TimeMapper.ToDto(tag);
    }
}
