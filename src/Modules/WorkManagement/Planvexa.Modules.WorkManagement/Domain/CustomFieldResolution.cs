namespace Planvexa.Modules.WorkManagement.Domain;

/// <summary>
/// Resolves the effective set of custom-field definitions visible on a List — workspace-wide
/// fields, the List's Space, every ancestor Folder of the List (root to immediate parent), and the List
/// itself. Pure (no I/O) so it is trivially unit-testable; callers (CustomFieldService) load the
/// candidate definitions and the List's ancestor-folder-id chain, then filter with this.
/// </summary>
public static class CustomFieldResolution
{
    public static IReadOnlyList<CustomFieldDefinition> EffectiveForList(
        IEnumerable<CustomFieldDefinition> allDefinitions,
        Guid spaceId,
        IReadOnlyCollection<Guid> ancestorFolderIds,
        Guid listId)
    {
        return allDefinitions
            .Where(d => IsInScope(d, spaceId, ancestorFolderIds, listId))
            .OrderBy(d => d.Scope)
            .ThenBy(d => d.Position)
            .ToList();
    }

    private static bool IsInScope(
        CustomFieldDefinition definition, Guid spaceId, IReadOnlyCollection<Guid> ancestorFolderIds, Guid listId)
        => definition.Scope switch
        {
            CustomFieldScope.Workspace => true,
            CustomFieldScope.Space => definition.ScopeId == spaceId,
            CustomFieldScope.Folder => definition.ScopeId is { } folderId && ancestorFolderIds.Contains(folderId),
            CustomFieldScope.List => definition.ScopeId == listId,
            _ => false,
        };
}

/// <summary>
/// Pure id-remapping for Folder/List duplication. A copied task keeps its parent-subtask
/// relationship only when the parent was copied too (a parent outside the copied set is dropped rather
/// than left dangling). Pure so the remap rule is unit-testable without a database.
/// </summary>
public static class TaskDuplicationMapping
{
    public static IReadOnlyDictionary<Guid, Guid> BuildIdMap(IEnumerable<Guid> sourceTaskIds, Func<Guid> newId)
    {
        var map = new Dictionary<Guid, Guid>();
        foreach (var id in sourceTaskIds)
        {
            map[id] = newId();
        }

        return map;
    }

    public static Guid? RemapParent(Guid? sourceParentId, IReadOnlyDictionary<Guid, Guid> idMap)
        => sourceParentId is { } parentId && idMap.TryGetValue(parentId, out var mapped) ? mapped : null;
}
