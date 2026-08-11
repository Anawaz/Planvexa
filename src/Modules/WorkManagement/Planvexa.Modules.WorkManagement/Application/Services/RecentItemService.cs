namespace Planvexa.Modules.WorkManagement.Application.Services;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;

/// <summary>
/// P8: tracks a user's recently-viewed resources across the app (any resource_type, same free-form
/// convention as <see cref="WorkFavorite"/>) so the command palette / a sidebar section can offer "jump
/// back to what you had open" without re-searching. Upsert-on-view — repeat views just bump
/// <see cref="RecentItem.ViewedAtUtc"/> — capped at <see cref="MaxPerUser"/> rows per user, oldest
/// dropped first.
/// </summary>
public sealed class RecentItemService(
    WorkServiceContext ctx, IRecentItemStore store,
    ISpaceStore spaces, IFolderStore folders, ITaskListStore lists, IWorkItemStore tasks)
    : WorkServiceBase(ctx)
{
    public const int MaxPerUser = 50;
    public const int DefaultLimit = 20;

    public async Task RecordViewAsync(RecordRecentItemCommand command, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var existing = await store.FindAsync(workspaceId, UserId, command.ResourceType, command.ResourceId, ct);
        if (existing is not null)
        {
            existing.Touch(Now);
            await SaveAsync(ct);
            return;
        }

        store.Add(RecentItem.Create(NewId(), workspaceId, UserId, command.ResourceType, command.ResourceId, Now));
        foreach (var overflow in await store.ListOverflowAsync(workspaceId, UserId, MaxPerUser, ct))
        {
            store.Remove(overflow);
        }

        try
        {
            await SaveAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two "record this view" requests for the same resource can race (e.g. rapid re-navigation
            // firing the tracking call twice) — the find-then-insert above isn't atomic under concurrent
            // requests, so the second insert can violate the (workspace, user, resource) unique
            // constraint. Losing this race is not an error to the caller: whichever request's insert
            // committed first already recorded this resource as recently viewed, which is exactly what
            // this call was trying to achieve — just swallow it rather than surface a 500. Catching the
            // provider-agnostic DbUpdateException (not a Postgres-specific type) keeps this Application-
            // layer service free of an Infrastructure-layer (Npgsql) reference, per the modular-monolith
            // boundary rules; this is the terminal DB operation for this request (the endpoint returns
            // immediately after), so there's no later save in this scope the still-tracked failed insert
            // could corrupt.
        }
    }

    public async Task<IReadOnlyList<RecentItemDto>> ListAsync(int? limit, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxPerUser);

        // Small overfetch: some rows may now be invisible (access revoked, resource deleted) since they
        // were last viewed and get filtered out below without shrinking the page below `take`.
        var candidates = await store.ListForUserAsync(workspaceId, UserId, take * 2, ct);

        var result = new List<RecentItemDto>();
        foreach (var item in candidates)
        {
            if (result.Count >= take)
            {
                break;
            }

            if (await IsStillVisibleAsync(item, ct))
            {
                result.Add(new RecentItemDto(item.ResourceType, item.ResourceId, item.ViewedAtUtc));
            }
        }

        return result;
    }

    // ponytail: only re-verifies WorkManagement-owned resource types (task/list/space/folder), since this
    // module already has the entity stores + authorizer for them. A recent item for a resource type owned
    // by another module (Document/Dashboard/ChatChannel/Form) is trusted as of the view — its own read
    // endpoint still enforces permission independently when actually opened — rather than re-checked here.
    // Upgrade path if stale titles from access-revoked resources prove a problem: a cross-module
    // recent-items visibility check mirroring SearchAggregator's ISearchProvider fan-out.
    private async Task<bool> IsStillVisibleAsync(RecentItem item, CancellationToken ct) => item.ResourceType switch
    {
        WorkResourceTypes.Space => await IsVisibleAsync(await spaces.FindAsync(item.ResourceId, ct), WorkResourceTypes.Space, ct),
        WorkResourceTypes.Folder => await IsVisibleAsync(await folders.FindAsync(item.ResourceId, ct), WorkResourceTypes.Folder, ct),
        WorkResourceTypes.List => await IsVisibleAsync(await lists.FindAsync(item.ResourceId, ct), WorkResourceTypes.List, ct),
        WorkResourceTypes.Task => await IsVisibleAsync(await tasks.FindAsync(item.ResourceId, ct), WorkResourceTypes.Task, ct),
        _ => true,
    };

    private async Task<bool> IsVisibleAsync(WorkEntity? resource, string resourceType, CancellationToken ct)
        => resource is not null && await CanReadAsync(resource, resourceType, ct);
}
