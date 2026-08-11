namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;

public sealed class SpaceService(WorkServiceContext ctx, ISpaceStore spaces) : WorkServiceBase(ctx)
{
    public async Task<SpaceDto> CreateAsync(CreateSpaceCommand command, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureManageStructure((await AccessAsync(workspaceId, ct))?.Role);

        var max = await spaces.MaxPositionAsync(workspaceId, ct);
        var space = Space.Create(NewId(), workspaceId, command.Name, Positioning.Append(max), UserId, Now);
        space.Update(null, command.Description, command.Color, command.Icon, UserId, Now);

        spaces.Add(space);
        Audit("space.created", nameof(Space), space.Id, new { command.Name, workspaceId });
        await SaveAsync(ct);
        return WorkMapper.ToDto(space);
    }

    public async Task<IReadOnlyList<SpaceDto>> ListAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var list = await spaces.ListByWorkspaceAsync(workspaceId, ct);
        var visible = new List<SpaceDto>(list.Count);
        foreach (var space in list.Where(s => !s.IsDeleted))
        {
            // ADR-0003: private spaces are filtered out unless the caller has an ACL grant.
            // CanReadAsync is a no-op (no extra query) for the common non-private, no-ACL case.
            if (await CanReadAsync(space, WorkResourceTypes.Space, ct))
            {
                visible.Add(WorkMapper.ToDto(space));
            }
        }

        return visible;
    }

    public async Task<SpaceDto> UpdateAsync(Guid spaceId, UpdateSpaceCommand command, CancellationToken ct = default)
    {
        var space = await LoadForManageAsync(spaceId, ct);
        space.Update(command.Name, command.Description, command.Color, command.Icon, UserId, Now);
        if (command.Position.HasValue)
        {
            space.Reposition(command.Position.Value);
        }

        Audit("space.updated", nameof(Space), space.Id);
        await SaveAsync(ct);
        return WorkMapper.ToDto(space);
    }

    public async Task ArchiveAsync(Guid spaceId, bool archive, CancellationToken ct = default)
    {
        var space = await LoadForManageAsync(spaceId, ct);
        if (archive)
        {
            space.Archive();
        }
        else
        {
            space.Unarchive();
        }

        Audit(archive ? "space.archived" : "space.unarchived", nameof(Space), space.Id);
        await SaveAsync(ct);
    }

    public async Task DeleteAsync(Guid spaceId, CancellationToken ct = default)
    {
        var space = await LoadForManageAsync(spaceId, ct);
        space.SoftDelete(UserId, Now);
        Audit("space.deleted", nameof(Space), space.Id);
        await SaveAsync(ct);
    }

    public async Task RestoreAsync(Guid spaceId, CancellationToken ct = default)
    {
        var space = await spaces.FindAsync(spaceId, ct) ?? throw new NotFoundException("Space not found.");
        await EnsureManageStructureAsync(space, WorkResourceTypes.Space, ct);
        space.Restore();
        Audit("space.restored", nameof(Space), space.Id);
        await SaveAsync(ct);
    }

    public async Task<SpaceDto> SetDefaultViewAsync(Guid spaceId, Guid? viewId, CancellationToken ct = default)
    {
        var space = await LoadForManageAsync(spaceId, ct);
        space.SetDefaultView(viewId, UserId, Now);
        Audit("space.default_view_set", nameof(Space), space.Id, new { viewId });
        await SaveAsync(ct);
        return WorkMapper.ToDto(space);
    }

    private async Task<Space> LoadForManageAsync(Guid spaceId, CancellationToken ct)
    {
        var space = await spaces.FindAsync(spaceId, ct);
        if (space is null || space.IsDeleted)
        {
            throw new NotFoundException("Space not found.");
        }

        await EnsureManageStructureAsync(space, WorkResourceTypes.Space, ct);
        return space;
    }
}

/// <summary>Folders nest to arbitrary depth; cycle prevention lives in <see cref="FolderHierarchy"/>.</summary>
public sealed class FolderService(
    WorkServiceContext ctx, ISpaceStore spaces, IFolderStore folders, ITaskListStore lists, TaskListService listService) : WorkServiceBase(ctx)
{
    public async Task<FolderDto> CreateAsync(CreateFolderCommand command, CancellationToken ct = default)
    {
        var space = await spaces.FindAsync(command.SpaceId, ct);
        if (space is null || space.IsDeleted)
        {
            throw new NotFoundException("Space not found.");
        }

        await EnsureManageStructureAsync(space, WorkResourceTypes.Space, ct);

        if (command.ParentFolderId is { } parentFolderId)
        {
            var parent = await folders.FindAsync(parentFolderId, ct);
            if (parent is null || parent.IsDeleted || parent.SpaceId != space.Id)
            {
                throw new NotFoundException("Parent folder not found in this space.");
            }
        }

        var max = await folders.MaxPositionAsync(space.Id, ct);
        var folder = Folder.Create(NewId(), space.WorkspaceId, space.Id, command.ParentFolderId, command.Name, Positioning.Append(max), UserId, Now);
        folders.Add(folder);
        Audit("folder.created", nameof(Folder), folder.Id, new { command.Name, spaceId = space.Id, parentFolderId = command.ParentFolderId });
        await SaveAsync(ct);
        return WorkMapper.ToDto(folder);
    }

    public async Task<IReadOnlyList<FolderDto>> ListAsync(Guid spaceId, CancellationToken ct = default)
    {
        var space = await spaces.FindAsync(spaceId, ct) ?? throw new NotFoundException("Space not found.");
        await EnsureReadAsync(space, WorkResourceTypes.Space, ct);

        var list = await folders.ListBySpaceAsync(spaceId, ct);
        var visible = new List<FolderDto>(list.Count);
        foreach (var folder in list.Where(f => !f.IsDeleted))
        {
            if (await CanReadAsync(folder, WorkResourceTypes.Folder, ct))
            {
                visible.Add(WorkMapper.ToDto(folder));
            }
        }

        return visible;
    }

    /// <summary>Direct-by-id read, gated the same way as the listing (own IsPrivate/ACL + ancestor-privacy probe).</summary>
    public async Task<FolderDto> GetAsync(Guid folderId, CancellationToken ct = default)
    {
        var folder = await folders.FindAsync(folderId, ct);
        if (folder is null || folder.IsDeleted)
        {
            throw new NotFoundException("Folder not found.");
        }

        await EnsureReadAsync(folder, WorkResourceTypes.Folder, ct);
        return WorkMapper.ToDto(folder);
    }

    public async Task<FolderDto> RenameAsync(Guid folderId, string name, CancellationToken ct = default)
    {
        var folder = await LoadForManageAsync(folderId, ct);
        folder.Rename(name, UserId, Now);
        Audit("folder.renamed", nameof(Folder), folder.Id, new { name });
        await SaveAsync(ct);
        return WorkMapper.ToDto(folder);
    }

    public async Task ArchiveAsync(Guid folderId, bool archive, CancellationToken ct = default)
    {
        var folder = await LoadForManageAsync(folderId, ct);
        if (archive)
        {
            folder.Archive();
        }
        else
        {
            folder.Unarchive();
        }

        Audit(archive ? "folder.archived" : "folder.unarchived", nameof(Folder), folder.Id);
        await SaveAsync(ct);
    }

    public async Task RestoreAsync(Guid folderId, CancellationToken ct = default)
    {
        var folder = await folders.FindAsync(folderId, ct) ?? throw new NotFoundException("Folder not found.");
        await EnsureManageStructureAsync(folder, WorkResourceTypes.Folder, ct);
        folder.Restore();
        Audit("folder.restored", nameof(Folder), folder.Id);
        await SaveAsync(ct);
    }

    public async Task<FolderDto> ReorderAsync(Guid folderId, double position, CancellationToken ct = default)
    {
        var folder = await LoadForManageAsync(folderId, ct);
        folder.Reposition(position);
        Audit("folder.reordered", nameof(Folder), folder.Id, new { position });
        await SaveAsync(ct);
        return WorkMapper.ToDto(folder);
    }

    public async Task DeleteAsync(Guid folderId, CancellationToken ct = default)
    {
        var folder = await LoadForManageAsync(folderId, ct);

        var spaceFolders = await folders.ListBySpaceAsync(folder.SpaceId, ct);
        if (spaceFolders.Any(f => !f.IsDeleted && f.ParentFolderId == folder.Id))
        {
            throw new ConflictException("Move or delete this folder's subfolders first.");
        }

        var spaceLists = await lists.ListBySpaceAsync(folder.SpaceId, ct);
        if (spaceLists.Any(l => !l.IsDeleted && l.FolderId == folder.Id))
        {
            throw new ConflictException("Move or delete this folder's lists first.");
        }

        folder.SoftDelete(UserId, Now);
        Audit("folder.deleted", nameof(Folder), folder.Id);
        await SaveAsync(ct);
    }

    /// <summary>
    /// Re-parents a folder (or moves it to top-level with <c>newParentFolderId: null</c>). Rejects the
    /// move with a <see cref="ValidationAppException"/> when it would make the folder its own ancestor —
    /// enforced here (not just left to the absence of a "move under descendant" UI action).
    /// </summary>
    public async Task<FolderDto> MoveAsync(Guid folderId, Guid? newParentFolderId, CancellationToken ct = default)
    {
        var folder = await LoadForManageAsync(folderId, ct);

        if (newParentFolderId == folder.ParentFolderId)
        {
            return WorkMapper.ToDto(folder);
        }

        if (newParentFolderId is { } parentFolderId)
        {
            var newParent = await folders.FindAsync(parentFolderId, ct);
            if (newParent is null || newParent.IsDeleted || newParent.SpaceId != folder.SpaceId)
            {
                throw new NotFoundException("Parent folder not found in this space.");
            }
        }

        var siblings = await folders.ListBySpaceAsync(folder.SpaceId, ct);
        var parentById = siblings.Where(f => !f.IsDeleted).ToDictionary(f => f.Id, f => f.ParentFolderId);
        if (FolderHierarchy.CreatesCycle(folder.Id, newParentFolderId, parentById))
        {
            throw new ValidationAppException("Moving this folder here would create a cycle: a folder cannot become its own ancestor.");
        }

        folder.Reparent(newParentFolderId, UserId, Now);
        Audit("folder.moved", nameof(Folder), folder.Id, new { newParentFolderId });
        await SaveAsync(ct);
        return WorkMapper.ToDto(folder);
    }

    public async Task<FolderDto> SetDefaultViewAsync(Guid folderId, Guid? viewId, CancellationToken ct = default)
    {
        var folder = await LoadForManageAsync(folderId, ct);
        folder.SetDefaultView(viewId, UserId, Now);
        Audit("folder.default_view_set", nameof(Folder), folder.Id, new { viewId });
        await SaveAsync(ct);
        return WorkMapper.ToDto(folder);
    }

    /// <summary>
    /// Deep-copies a folder — every descendant subfolder (to arbitrary depth) and every list they
    /// contain, with each list's tasks copied the same way <see cref="TaskListService.DuplicateAsync"/>
    /// copies a single list (see its doc comment for exactly what is/isn't carried over). Only the root
    /// copy's name gets the "(Copy)" suffix; nested folders/lists keep their source name.
    /// </summary>
    public async Task<FolderDto> DuplicateAsync(Guid folderId, CancellationToken ct = default)
    {
        var source = await LoadForManageAsync(folderId, ct);

        var spaceFolders = await folders.ListBySpaceAsync(source.SpaceId, ct);
        var spaceLists = await lists.ListBySpaceAsync(source.SpaceId, ct);

        var copy = await CopyFolderRecursiveAsync(
            source, source.ParentFolderId, $"{source.Name} (Copy)", spaceFolders, spaceLists, ct, depth: 0);

        Audit("folder.duplicated", nameof(Folder), copy.Id, new { sourceFolderId = source.Id });
        await SaveAsync(ct);
        return WorkMapper.ToDto(copy);
    }

    private async Task<Folder> CopyFolderRecursiveAsync(
        Folder source, Guid? targetParentFolderId, string name,
        IReadOnlyList<Folder> spaceFolders, IReadOnlyList<TaskList> spaceLists, CancellationToken ct, int depth)
    {
        if (depth > 64)
        {
            throw new ValidationAppException("Folder structure is too deep to duplicate.");
        }

        var maxPos = await folders.MaxPositionAsync(source.SpaceId, ct);
        var copy = Folder.Create(NewId(), source.WorkspaceId, source.SpaceId, targetParentFolderId, name, Positioning.Append(maxPos), UserId, Now);
        folders.Add(copy);

        foreach (var list in spaceLists.Where(l => !l.IsDeleted && l.FolderId == source.Id).OrderBy(l => l.Position))
        {
            await listService.CopyListContentsAsync(list, source.SpaceId, copy.Id, list.Name, ct);
        }

        foreach (var child in spaceFolders.Where(f => !f.IsDeleted && f.ParentFolderId == source.Id).OrderBy(f => f.Position))
        {
            await CopyFolderRecursiveAsync(child, copy.Id, child.Name, spaceFolders, spaceLists, ct, depth + 1);
        }

        return copy;
    }

    private async Task<Folder> LoadForManageAsync(Guid folderId, CancellationToken ct)
    {
        var folder = await folders.FindAsync(folderId, ct);
        if (folder is null || folder.IsDeleted)
        {
            throw new NotFoundException("Folder not found.");
        }

        await EnsureManageStructureAsync(folder, WorkResourceTypes.Folder, ct);
        return folder;
    }
}
