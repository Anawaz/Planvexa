namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.SharedContracts.Search;

/// <summary>
/// Global "search or jump to" over one workspace: tasks, lists, folders and spaces by name/title.
///
/// SECURITY: every candidate is run through <see cref="WorkServiceBase.CanReadAsync"/> before
/// it is included in a result. Search now feeds the cross-module search aggregator (apps/api's
/// SearchAggregator), so this is no longer "just" a workspace-role-gated convenience box — a private
/// Space/Folder/List/Task without a grant for the caller must never surface here. (Before cross-module search this
/// method only checked <see cref="WorkManagementAuthorizer.EnsureRead"/>, i.e. coarse workspace
/// membership, and returned raw ILIKE matches with no per-item privacy check at all — the same
/// confidentiality bug shape found and fixed in Gantt/Calendar/Activity in earlier work.)
/// </summary>
public sealed class SearchService(
    WorkServiceContext ctx, ISearchStore store, ISpaceStore spaces, ITaskListStore lists)
    : WorkServiceBase(ctx), ISearchProvider
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    /// <summary>Shorter terms match almost everything, so they are answered with nothing.</summary>
    private const int MinTermLength = 2;

    /// <summary>Longer terms cannot match anything a title holds; truncating keeps the LIKE bounded.</summary>
    private const int MaxTermLength = 128;

    /// <summary>Jump targets (space/folder/list) never crowd out task hits: each gets at most this many rows.</summary>
    private const int JumpTargetCap = 5;

    /// <summary>
    /// Privacy filtering happens after the SQL name/title match (it needs the full entity, not a
    /// projection), so candidates are over-fetched by this factor to absorb items the caller can't read
    /// without falling short of the requested page. Not a hard guarantee on a workspace where most
    /// matches are private to someone else — acceptable for a "jump to" box, not a paged listing.
    /// </summary>
    private const int OverfetchFactor = 3;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string? term, int? limit, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);
        var normalized = Normalize(term);
        if (normalized is null)
        {
            return [];
        }

        return await SearchInternalAsync(workspaceId, normalized, Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit), ct);
    }

    /// <summary>
    /// <see cref="ISearchProvider"/> — called by the cross-module search aggregator, which has already
    /// trimmed/length-validated <paramref name="term"/>. Still re-checks workspace membership: the
    /// aggregator runs once per request, not once per provider.
    /// </summary>
    async Task<IReadOnlyList<SearchHit>> ISearchProvider.SearchAsync(string term, int limit, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        if ((await AccessAsync(workspaceId, ct))?.Role is null)
        {
            return [];
        }

        return await SearchInternalAsync(workspaceId, term, limit, ct);
    }

    private async Task<IReadOnlyList<SearchHit>> SearchInternalAsync(Guid workspaceId, string term, int limit, CancellationToken ct)
    {
        var escaped = term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var contains = $"%{escaped}%";
        var startsWith = $"{escaped}%";
        var jumpCap = Math.Min(limit, JumpTargetCap);

        var hits = new List<SearchHit>();

        var spaceCount = 0;
        foreach (var space in await store.SearchSpacesAsync(workspaceId, contains, jumpCap * OverfetchFactor, ct))
        {
            if (spaceCount >= jumpCap)
            {
                break;
            }

            if (await CanReadAsync(space, WorkResourceTypes.Space, ct))
            {
                hits.Add(new SearchHit("Space", space.Id, space.Name, null, null));
                spaceCount++;
            }
        }

        var folderCount = 0;
        foreach (var folder in await store.SearchFoldersAsync(workspaceId, contains, jumpCap * OverfetchFactor, ct))
        {
            if (folderCount >= jumpCap)
            {
                break;
            }

            if (await CanReadAsync(folder, WorkResourceTypes.Folder, ct))
            {
                var spaceName = (await spaces.FindAsync(folder.SpaceId, ct))?.Name;
                hits.Add(new SearchHit("Folder", folder.Id, folder.Name, spaceName, null));
                folderCount++;
            }
        }

        var listCount = 0;
        foreach (var list in await store.SearchListsAsync(workspaceId, contains, jumpCap * OverfetchFactor, ct))
        {
            if (listCount >= jumpCap)
            {
                break;
            }

            if (await CanReadAsync(list, WorkResourceTypes.List, ct))
            {
                var spaceName = (await spaces.FindAsync(list.SpaceId, ct))?.Name;
                hits.Add(new SearchHit("List", list.Id, list.Name, spaceName, list.Id));
                listCount++;
            }
        }

        var taskBudget = limit - hits.Count;
        if (taskBudget > 0)
        {
            var taskCount = 0;
            foreach (var task in await store.SearchTasksAsync(workspaceId, contains, startsWith, taskBudget * OverfetchFactor, ct))
            {
                if (taskCount >= taskBudget)
                {
                    break;
                }

                if (await CanReadAsync(task, WorkResourceTypes.Task, ct))
                {
                    var listName = (await lists.FindAsync(task.ListId, ct))?.Name;
                    hits.Add(new SearchHit("Task", task.Id, task.Title, listName, task.ListId));
                    taskCount++;
                }
            }
        }

        return hits;
    }

    private static string? Normalize(string? term)
    {
        var trimmed = (term ?? string.Empty).Trim();
        if (trimmed.Length < MinTermLength)
        {
            return null;
        }

        return trimmed.Length > MaxTermLength ? trimmed[..MaxTermLength] : trimmed;
    }
}
