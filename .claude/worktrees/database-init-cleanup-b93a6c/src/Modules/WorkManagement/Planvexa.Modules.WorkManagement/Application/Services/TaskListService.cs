namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;

public sealed class TaskListService(
    WorkServiceContext ctx,
    ISpaceStore spaces,
    IFolderStore folders,
    ITaskListStore lists,
    IStatusSchemeStore schemes,
    WorkspaceProvisioningService provisioning,
    IWorkItemStore tasks,
    IChecklistStore checklists,
    ICustomFieldStore customFields,
    ITaskListMembershipStore memberships) : WorkServiceBase(ctx)
{
    public async Task<ListDto> CreateAsync(CreateListCommand command, CancellationToken ct = default)
    {
        var space = await spaces.FindAsync(command.SpaceId, ct);
        if (space is null || space.IsDeleted)
        {
            throw new NotFoundException("Space not found.");
        }

        await EnsureManageStructureAsync(space, WorkResourceTypes.Space, ct);

        if (command.FolderId is { } folderId)
        {
            var folder = await folders.FindAsync(folderId, ct);
            if (folder is null || folder.SpaceId != space.Id)
            {
                throw new NotFoundException("Folder not found in this space.");
            }
        }

        Guid schemeId;
        if (command.StatusSchemeId is { } requestedScheme)
        {
            var scheme = await schemes.FindAsync(requestedScheme, ct)
                ?? throw new NotFoundException("Status scheme not found.");
            schemeId = scheme.Id;
        }
        else
        {
            var defaultScheme = await provisioning.EnsureDefaultSchemeAsync(space.WorkspaceId, ct);
            schemeId = defaultScheme.Id;
        }

        var max = await lists.MaxPositionAsync(space.Id, ct);
        var list = TaskList.Create(
            NewId(), space.WorkspaceId, space.Id, command.FolderId, command.Name, schemeId, Positioning.Append(max), UserId, Now);
        list.Update(null, command.Description, UserId, Now);

        lists.Add(list);
        Audit("list.created", nameof(TaskList), list.Id, new { command.Name, spaceId = space.Id });
        await SaveAsync(ct);
        return WorkMapper.ToDto(list);
    }

    public async Task<IReadOnlyList<ListDto>> ListBySpaceAsync(Guid spaceId, CancellationToken ct = default)
    {
        var space = await spaces.FindAsync(spaceId, ct) ?? throw new NotFoundException("Space not found.");
        await EnsureReadAsync(space, WorkResourceTypes.Space, ct);

        var result = await lists.ListBySpaceAsync(spaceId, ct);
        var visible = new List<ListDto>(result.Count);
        foreach (var list in result.Where(l => !l.IsDeleted))
        {
            if (await CanReadAsync(list, WorkResourceTypes.List, ct))
            {
                visible.Add(WorkMapper.ToDto(list));
            }
        }

        return visible;
    }

    public async Task<ListDto> GetAsync(Guid listId, CancellationToken ct = default)
    {
        var list = await lists.FindAsync(listId, ct) ?? throw new NotFoundException("List not found.");
        await EnsureReadAsync(list, WorkResourceTypes.List, ct);
        return WorkMapper.ToDto(list);
    }

    public async Task<ListDto> UpdateAsync(Guid listId, UpdateListCommand command, CancellationToken ct = default)
    {
        var list = await LoadForManageAsync(listId, ct);
        list.Update(command.Name, command.Description, UserId, Now);
        Audit("list.updated", nameof(TaskList), list.Id);
        await SaveAsync(ct);
        return WorkMapper.ToDto(list);
    }

    public async Task ArchiveAsync(Guid listId, bool archive, CancellationToken ct = default)
    {
        var list = await LoadForManageAsync(listId, ct);
        if (archive)
        {
            list.Archive();
        }
        else
        {
            list.Unarchive();
        }

        Audit(archive ? "list.archived" : "list.unarchived", nameof(TaskList), list.Id);
        await SaveAsync(ct);
    }

    public async Task DeleteAsync(Guid listId, CancellationToken ct = default)
    {
        var list = await LoadForManageAsync(listId, ct);
        list.SoftDelete(UserId, Now);
        Audit("list.deleted", nameof(TaskList), list.Id);
        await SaveAsync(ct);
    }

    public async Task RestoreAsync(Guid listId, CancellationToken ct = default)
    {
        var list = await lists.FindAsync(listId, ct) ?? throw new NotFoundException("List not found.");
        await EnsureManageStructureAsync(list, WorkResourceTypes.List, ct);
        list.Restore();
        Audit("list.restored", nameof(TaskList), list.Id);
        await SaveAsync(ct);
    }

    public async Task<ListDto> SetDefaultViewAsync(Guid listId, Guid? viewId, CancellationToken ct = default)
    {
        var list = await LoadForManageAsync(listId, ct);
        list.SetDefaultView(viewId, UserId, Now);
        Audit("list.default_view_set", nameof(TaskList), list.Id, new { viewId });
        await SaveAsync(ct);
        return WorkMapper.ToDto(list);
    }

    /// <summary>
    /// Creates a copy of the list (title suffixed "(Copy)") in the same space/folder, with every
    /// non-deleted task copied the way <see cref="WorkItemService.DuplicateAsync"/> copies a single task
    /// (fields, assignees, watchers, tags, checklists, custom-field values — not dependencies or
    /// attachments), plus subtask parent/child relationships preserved among the copied tasks.
    /// </summary>
    public async Task<ListDto> DuplicateAsync(Guid listId, CancellationToken ct = default)
    {
        var source = await lists.FindAsync(listId, ct);
        if (source is null || source.IsDeleted)
        {
            throw new NotFoundException("List not found.");
        }

        await EnsureManageStructureAsync(source, WorkResourceTypes.List, ct);

        var copy = await CopyListContentsAsync(source, source.SpaceId, source.FolderId, $"{source.Name} (Copy)", ct);
        Audit("list.duplicated", nameof(TaskList), copy.Id, new { sourceListId = source.Id });
        await SaveAsync(ct);
        return WorkMapper.ToDto(copy);
    }

    /// <summary>
    /// Shared copy engine for both a direct List duplicate and a Folder duplicate's per-list step
    /// (<see cref="FolderService.DuplicateAsync"/>) — internal so FolderService can reuse it rather than
    /// re-implementing task-copy semantics. Callers are responsible for authorization and Audit/SaveAsync.
    /// </summary>
    internal async Task<TaskList> CopyListContentsAsync(
        TaskList source, Guid targetSpaceId, Guid? targetFolderId, string name, CancellationToken ct)
    {
        var maxPos = await lists.MaxPositionAsync(targetSpaceId, ct);
        var copy = TaskList.Create(
            NewId(), source.WorkspaceId, targetSpaceId, targetFolderId, name, source.StatusSchemeId, Positioning.Append(maxPos), UserId, Now);
        lists.Add(copy);

        var sourceTasks = (await tasks.ListByListAsync(source.Id, ct)).Where(t => !t.IsDeleted).OrderBy(t => t.Position).ToList();
        var idMap = TaskDuplicationMapping.BuildIdMap(sourceTasks.Select(t => t.Id), NewId);

        foreach (var sourceTask in sourceTasks)
        {
            var full = await tasks.FindWithRelationsAsync(sourceTask.Id, ct) ?? sourceTask;
            var newId = idMap[sourceTask.Id];
            var newParentId = TaskDuplicationMapping.RemapParent(full.ParentId, idMap);
            var sequence = copy.NextTaskSequence();

            var newTask = WorkItem.Create(
                newId, copy.WorkspaceId, targetSpaceId, copy.Id, newParentId,
                sequence, full.Title, full.StatusId, full.IsCompleted, full.Position, UserId, Now);
            newTask.UpdateDetails(null, full.Description, full.Priority, full.StartDate, full.DueDate, full.IsMilestone, UserId, Now);

            foreach (var assignee in full.Assignees)
            {
                newTask.AddAssignee(NewId(), assignee.UserId, UserId, Now);
            }

            foreach (var watcher in full.Watchers)
            {
                newTask.AddWatcher(NewId(), watcher.UserId, Now);
            }

            if (full.Tags.Count > 0)
            {
                newTask.SetTags(full.Tags.Select(t => t.TagId).ToList(), NewId, UserId, Now);
            }

            tasks.Add(newTask);
            memberships.Add(new TaskListMembership(NewId(), copy.WorkspaceId, newTask.Id, copy.Id, isPrimary: true, full.Position, Now));

            foreach (var sourceChecklist in await checklists.ListForTaskAsync(sourceTask.Id, ct))
            {
                var newChecklist = TaskChecklist.Create(NewId(), newId, sourceChecklist.Name, sourceChecklist.Position);
                foreach (var item in sourceChecklist.Items)
                {
                    var newItem = newChecklist.AddItem(NewId(), item.Content, item.Position);
                    if (item.IsResolved)
                    {
                        newItem.Update(null, true, null);
                    }
                }

                checklists.Add(newChecklist);
            }

            foreach (var value in await customFields.ListValuesForTaskAsync(sourceTask.Id, ct))
            {
                var newValue = CustomFieldValue.Create(NewId(), newId, value.DefinitionId);
                if (value.OptionId is not null) newValue.SetOption(value.OptionId, Now);
                else if (value.JsonValue is not null) newValue.SetMultiSelect(value.JsonValue, Now);
                else if (value.NumberValue is not null) newValue.SetNumber(value.NumberValue, Now);
                else if (value.DateValue is not null) newValue.SetDate(value.DateValue, Now);
                else if (value.BoolValue is not null) newValue.SetBool(value.BoolValue, Now);
                else newValue.SetText(value.TextValue, Now);
                customFields.AddValue(newValue);
            }
        }

        return copy;
    }

    private async Task<TaskList> LoadForManageAsync(Guid listId, CancellationToken ct)
    {
        var list = await lists.FindAsync(listId, ct);
        if (list is null || list.IsDeleted)
        {
            throw new NotFoundException("List not found.");
        }

        await EnsureManageStructureAsync(list, WorkResourceTypes.List, ct);
        return list;
    }
}

public sealed class StatusSchemeService(WorkServiceContext ctx, IStatusSchemeStore schemes, WorkspaceProvisioningService provisioning)
    : WorkServiceBase(ctx)
{
    public async Task<IReadOnlyList<StatusSchemeDto>> ListAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        // Guarantee a default exists so the workspace always has at least one scheme.
        await provisioning.EnsureDefaultSchemeAsync(workspaceId, ct);
        await SaveAsync(ct);

        var list = await schemes.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(WorkMapper.ToDto).ToList();
    }

    public async Task<StatusSchemeDto> CreateAsync(string name, IReadOnlyList<(string Name, StatusCategory Category, string? Color)> statuses, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureManageStructure((await AccessAsync(workspaceId, ct))?.Role);

        var scheme = StatusScheme.Create(NewId(), workspaceId, name, isDefault: false);
        double position = Positioning.Step;
        foreach (var s in statuses)
        {
            scheme.AddStatus(NewId(), s.Name, s.Category, string.IsNullOrWhiteSpace(s.Color) ? "#8b8b8b" : s.Color!, position);
            position += Positioning.Step;
        }

        schemes.Add(scheme);
        Audit("status_scheme.created", nameof(StatusScheme), scheme.Id, new { name });
        await SaveAsync(ct);
        return WorkMapper.ToDto(scheme);
    }
}
