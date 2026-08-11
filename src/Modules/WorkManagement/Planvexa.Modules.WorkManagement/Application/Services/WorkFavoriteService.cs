namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.Modules.WorkManagement.Authorization;

/// <summary>
/// Per-user favourites/bookmarks over any WorkManagement resource (Space/Folder/List today; free-form
/// resourceType so Task/View can favourite later with no schema change — see WorkFavorite). No existing
/// bookmark mechanism was found in WorkManagement before this (SavedView.IsPrivate is unrelated: it hides a
/// view from other users, it does not bookmark one).
/// </summary>
public sealed class WorkFavoriteService(WorkServiceContext ctx, IWorkFavoriteStore favorites) : WorkServiceBase(ctx)
{
    public async Task<IReadOnlyList<WorkFavoriteDto>> ListAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);
        var list = await favorites.ListForUserAsync(workspaceId, UserId, ct);
        return list.Select(WorkMapper.ToDto).ToList();
    }

    /// <summary>Toggles a favourite on/off; returns true if it is now favourited.</summary>
    public async Task<bool> ToggleAsync(ToggleFavoriteCommand command, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var existing = await favorites.FindAsync(workspaceId, UserId, command.ResourceType, command.ResourceId, ct);
        if (existing is not null)
        {
            favorites.Remove(existing);
            Audit("favorite.removed", command.ResourceType, command.ResourceId);
            await SaveAsync(ct);
            return false;
        }

        var favorite = Domain.WorkFavorite.Create(NewId(), workspaceId, UserId, command.ResourceType, command.ResourceId, Now);
        favorites.Add(favorite);
        Audit("favorite.added", command.ResourceType, command.ResourceId);
        await SaveAsync(ct);
        return true;
    }
}
