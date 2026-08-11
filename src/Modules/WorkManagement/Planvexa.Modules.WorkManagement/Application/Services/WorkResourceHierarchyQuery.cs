namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Implements the cross-module <see cref="IResourceHierarchyQuery"/> for WorkManagement's four ACL
/// resource types (ADR-0003), so Tenancy's resolver can walk Task→List→Folder→Space without
/// reading this module's tables directly (AGENTS.md rule 7).
/// </summary>
public sealed class WorkResourceHierarchyQuery(
    ISpaceStore spaces, IFolderStore folders, ITaskListStore lists, IWorkItemStore tasks) : IResourceHierarchyQuery
{
    public async Task<ResourceHierarchyNode?> GetAsync(
        string resourceType, Guid resourceId, CancellationToken cancellationToken = default)
    {
        switch (resourceType)
        {
            case WorkResourceTypes.Space:
                var space = await spaces.FindAsync(resourceId, cancellationToken);
                return space is null ? null : new ResourceHierarchyNode(space.WorkspaceId, space.IsPrivate, null, null, space.CreatedByUserId);

            case WorkResourceTypes.Folder:
                var folder = await folders.FindAsync(resourceId, cancellationToken);
                if (folder is null)
                {
                    return null;
                }

                // Folders now nest to arbitrary depth. A subfolder's parent in the ACL/privacy
                // chain is its parent Folder when it has one, and only falls back to the Space at the
                // root of the chain — previously this always pointed straight at the Space, silently
                // skipping every intermediate folder's own IsPrivate flag once nesting went beyond one
                // level (see WorkManagementAuthorizer's ancestor-privacy probe, which walks this chain).
                return folder.ParentFolderId is { } parentFolderId
                    ? new ResourceHierarchyNode(folder.WorkspaceId, folder.IsPrivate, WorkResourceTypes.Folder, parentFolderId, folder.CreatedByUserId)
                    : new ResourceHierarchyNode(folder.WorkspaceId, folder.IsPrivate, WorkResourceTypes.Space, folder.SpaceId, folder.CreatedByUserId);

            case WorkResourceTypes.List:
                var list = await lists.FindAsync(resourceId, cancellationToken);
                if (list is null)
                {
                    return null;
                }

                return list.FolderId is { } folderId
                    ? new ResourceHierarchyNode(list.WorkspaceId, list.IsPrivate, WorkResourceTypes.Folder, folderId, list.CreatedByUserId)
                    : new ResourceHierarchyNode(list.WorkspaceId, list.IsPrivate, WorkResourceTypes.Space, list.SpaceId, list.CreatedByUserId);

            case WorkResourceTypes.Task:
                var task = await tasks.FindAsync(resourceId, cancellationToken);
                return task is null
                    ? null
                    : new ResourceHierarchyNode(task.WorkspaceId, task.IsPrivate, WorkResourceTypes.List, task.ListId, task.CreatedByUserId);

            default:
                return null;
        }
    }
}
