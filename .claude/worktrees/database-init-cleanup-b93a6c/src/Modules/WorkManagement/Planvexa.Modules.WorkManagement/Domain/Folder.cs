namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>Optional grouping layer between a space and its lists.</summary>
public sealed class Folder : WorkEntity, IAggregateRoot
{
    private Folder()
    {
    }

    private Folder(Guid id, Guid workspaceId, Guid spaceId, Guid? parentFolderId, string name, DateTimeOffset nowUtc, Guid createdBy)
        : base(id)
    {
        WorkspaceId = workspaceId;
        SpaceId = spaceId;
        ParentFolderId = parentFolderId;
        Name = name;
        CreatedAtUtc = nowUtc;
        CreatedByUserId = createdBy;
    }

    public Guid SpaceId { get; private set; }

    /// <summary>Optional parent folder — folders may nest to arbitrary depth (null = top-level folder).</summary>
    public Guid? ParentFolderId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>The SavedView shown by default when a user opens this folder (null = fall back to the first view).</summary>
    public Guid? DefaultViewId { get; private set; }

    public static Folder Create(
        Guid id, Guid workspaceId, Guid spaceId, Guid? parentFolderId, string name, double position, Guid createdBy, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(spaceId, nameof(spaceId));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));

        return new Folder(id, workspaceId, spaceId, parentFolderId, name.Trim(), nowUtc, createdBy) { Position = position };
    }

    public void Rename(string name, Guid userId, DateTimeOffset nowUtc)
    {
        Name = Guard.AgainstNullOrWhiteSpace(name, nameof(name)).Trim();
        Touch(userId, nowUtc);
    }

    /// <summary>Re-parents this folder. Cycle prevention (a folder cannot become its own ancestor) is
    /// enforced by the caller (<see cref="FolderHierarchy.CreatesCycle"/>) before this is called, since
    /// that check needs the full folder set of the space.</summary>
    public void Reparent(Guid? parentFolderId, Guid userId, DateTimeOffset nowUtc)
    {
        ParentFolderId = parentFolderId;
        Touch(userId, nowUtc);
    }

    public void SetDefaultView(Guid? viewId, Guid userId, DateTimeOffset nowUtc)
    {
        DefaultViewId = viewId;
        Touch(userId, nowUtc);
    }
}

/// <summary>
/// Pure cycle-prevention for arbitrary-depth folder nesting. Given the parent-folder map of
/// every folder in a space, decides whether re-parenting one folder under another would make that
/// folder its own ancestor. No I/O — callers load the map once via <c>IFolderStore.ListBySpaceAsync</c>
/// and pass it in, which also keeps this trivially unit-testable.
/// </summary>
public static class FolderHierarchy
{
    public static bool CreatesCycle(Guid folderId, Guid? newParentFolderId, IReadOnlyDictionary<Guid, Guid?> parentById)
    {
        if (newParentFolderId is null)
        {
            return false;
        }

        if (newParentFolderId == folderId)
        {
            return true;
        }

        Guid? current = newParentFolderId;
        var hops = 0;
        while (current is { } id)
        {
            if (id == folderId)
            {
                return true;
            }

            if (!parentById.TryGetValue(id, out var next))
            {
                return false;
            }

            current = next;

            // Defensive: a parent chain longer than the folder count means the existing data already
            // cycles (should be impossible given this same guard governs every write) — treat as a cycle
            // rather than looping forever.
            if (++hops > parentById.Count)
            {
                return true;
            }
        }

        return false;
    }
}
