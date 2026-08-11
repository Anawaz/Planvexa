namespace Planvexa.Modules.Mobile.Application.Services;

using Planvexa.Modules.Mobile.Authorization;

public sealed class SyncService(MobileServiceContext ctx, Planvexa.SharedContracts.Mobile.IChangeFeed changeFeed)
    : MobileServiceBase(ctx)
{
    public async Task<SyncResultDto> GetChangesAsync(DateTimeOffset? sinceUtc, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        MobileAuthorizer.EnsureUse((await AccessAsync(workspaceId, ct))?.Role);

        var since = sinceUtc ?? DateTimeOffset.UnixEpoch;
        var page = await changeFeed.GetChangesAsync(workspaceId, since, 200, ct);
        var changes = page.Changes.Select(c => new SyncChangeDto(
            c.TaskId,
            c.ListId,
            c.SpaceId,
            c.Title,
            c.Priority,
            c.IsCompleted,
            c.IsDeleted,
            c.DueDate,
            c.ChangedAtUtc)).ToList();

        return new SyncResultDto(changes, page.NextCursorUtc);
    }
}
