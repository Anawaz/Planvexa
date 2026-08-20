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
            // The Space's own scheme when it has customized, otherwise the workspace default.
            var effective = await provisioning.EffectiveSchemeAsync(space, ct);
            schemeId = effective.Id;
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

    /// <summary>Moves the List to a different Space and/or Folder within the same Workspace, appending
    /// it after the target's existing lists. Both the List's current location and the target Space
    /// require manage-structure access.</summary>
    public async Task<ListDto> MoveAsync(Guid listId, Guid targetSpaceId, Guid? targetFolderId, CancellationToken ct = default)
    {
        var list = await LoadForManageAsync(listId, ct);
        var targetSpace = await RequireTargetSpaceAsync(list.WorkspaceId, targetSpaceId, targetFolderId, ct);

        var maxPos = await lists.MaxPositionAsync(targetSpace.Id, ct);
        list.MoveTo(targetSpace.Id, targetFolderId, Positioning.Append(maxPos), UserId, Now);
        Audit("list.moved", nameof(TaskList), list.Id, new { targetSpaceId = targetSpace.Id, targetFolderId });
        await SaveAsync(ct);
        return WorkMapper.ToDto(list);
    }

    /// <summary>Copies the List (and its tasks, via <see cref="CopyListContentsAsync"/>) into a
    /// different Space and/or Folder, leaving the source untouched — distinct from
    /// <see cref="DuplicateAsync"/>, which always copies in place.</summary>
    public async Task<ListDto> CopyToAsync(Guid listId, Guid targetSpaceId, Guid? targetFolderId, CancellationToken ct = default)
    {
        var source = await lists.FindAsync(listId, ct);
        if (source is null || source.IsDeleted)
        {
            throw new NotFoundException("List not found.");
        }

        await EnsureReadAsync(source, WorkResourceTypes.List, ct);
        var targetSpace = await RequireTargetSpaceAsync(source.WorkspaceId, targetSpaceId, targetFolderId, ct);

        var copy = await CopyListContentsAsync(source, targetSpace.Id, targetFolderId, source.Name, ct);
        Audit("list.copied", nameof(TaskList), copy.Id, new { sourceListId = source.Id, targetSpaceId = targetSpace.Id });
        await SaveAsync(ct);
        return WorkMapper.ToDto(copy);
    }

    private async Task<Space> RequireTargetSpaceAsync(Guid workspaceId, Guid targetSpaceId, Guid? targetFolderId, CancellationToken ct)
    {
        var targetSpace = await spaces.FindAsync(targetSpaceId, ct);
        if (targetSpace is null || targetSpace.IsDeleted || targetSpace.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Target space not found.");
        }

        await EnsureManageStructureAsync(targetSpace, WorkResourceTypes.Space, ct);

        if (targetFolderId is { } folderId)
        {
            var folder = await folders.FindAsync(folderId, ct);
            if (folder is null || folder.SpaceId != targetSpace.Id)
            {
                throw new NotFoundException("Folder not found in the target space.");
            }
        }

        return targetSpace;
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

public sealed class StatusSchemeService(
    WorkServiceContext ctx,
    IStatusSchemeStore schemes,
    WorkspaceProvisioningService provisioning,
    ISpaceStore spaces,
    ITaskListStore lists,
    IWorkItemStore tasks) : WorkServiceBase(ctx)
{
    public async Task<IReadOnlyList<StatusSchemeDto>> ListAsync(bool workspaceLevelOnly = false, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        // Guarantee a default exists so the workspace always has at least one scheme.
        await provisioning.EnsureDefaultSchemeAsync(workspaceId, ct);
        await SaveAsync(ct);

        var list = await schemes.ListByWorkspaceAsync(workspaceId, workspaceLevelOnly, ct);
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

    /// <summary>
    /// Configures the optional allowed-transitions restriction for one status (spec section 11). An
    /// empty <paramref name="toStatusIds"/> clears the restriction, making the status unrestricted again.
    /// </summary>
    public async Task<StatusSchemeDto> SetTransitionsAsync(Guid schemeId, Guid statusId, IReadOnlyList<Guid> toStatusIds, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureManageStructure((await AccessAsync(workspaceId, ct))?.Role);

        var scheme = await schemes.FindAsync(schemeId, ct) ?? throw new NotFoundException("Status scheme not found.");
        if (scheme.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Status scheme not found.");
        }

        scheme.SetAllowedTransitions(statusId, toStatusIds);
        Audit("status_scheme.transitions_set", nameof(StatusScheme), scheme.Id, new { statusId, toStatusIds });
        await SaveAsync(ct);
        return WorkMapper.ToDto(scheme);
    }

    public async Task<StatusSchemeDto> RenameAsync(Guid schemeId, string name, CancellationToken ct = default)
    {
        var scheme = await LoadForManageAsync(schemeId, ct);
        scheme.Rename(name);
        Audit("status_scheme.renamed", nameof(StatusScheme), scheme.Id, new { name });
        await SaveAsync(ct);
        return WorkMapper.ToDto(scheme);
    }

    public async Task<StatusSchemeDto> AddStatusAsync(
        Guid schemeId, string name, StatusCategory category, string? color, CancellationToken ct = default)
    {
        var scheme = await LoadForManageAsync(schemeId, ct);
        var max = scheme.Statuses.Count == 0 ? (double?)null : scheme.Statuses.Max(s => s.Position);
        var status = scheme.AddStatus(
            NewId(), name, category, string.IsNullOrWhiteSpace(color) ? "#8b8b8b" : color!, Positioning.Append(max));

        Audit("status_scheme.status_added", nameof(StatusScheme), scheme.Id, new { statusId = status.Id, name, category });
        await SaveAsync(ct);
        return WorkMapper.ToDto(scheme);
    }

    public async Task<StatusSchemeDto> UpdateStatusAsync(
        Guid schemeId, Guid statusId, string? name, StatusCategory? category, string? color, CancellationToken ct = default)
    {
        var scheme = await LoadForManageAsync(schemeId, ct);
        scheme.UpdateStatus(statusId, name, category, color);
        Audit("status_scheme.status_updated", nameof(StatusScheme), scheme.Id, new { statusId, name, category, color });
        await SaveAsync(ct);
        return WorkMapper.ToDto(scheme);
    }

    public async Task<StatusSchemeDto> MoveStatusAsync(Guid schemeId, Guid statusId, int newIndex, CancellationToken ct = default)
    {
        var scheme = await LoadForManageAsync(schemeId, ct);
        scheme.MoveStatus(statusId, newIndex);
        Audit("status_scheme.status_moved", nameof(StatusScheme), scheme.Id, new { statusId, newIndex });
        await SaveAsync(ct);
        return WorkMapper.ToDto(scheme);
    }

    /// <summary>
    /// Removes a status after moving every task sitting on it to <paramref name="moveTasksToStatusId"/> —
    /// the caller must name the replacement, there is no implicit fallback.
    /// </summary>
    public async Task<StatusSchemeDto> RemoveStatusAsync(
        Guid schemeId, Guid statusId, Guid moveTasksToStatusId, CancellationToken ct = default)
    {
        var scheme = await LoadForManageAsync(schemeId, ct);
        if (scheme.Statuses.All(s => s.Id != statusId))
        {
            throw new ValidationAppException("The status does not belong to this scheme.");
        }

        // Checked before the replacement is validated: when this is the only status there is no legal
        // replacement to name, so "you cannot remove it at all" is the honest answer.
        if (scheme.Statuses.Count == 1)
        {
            throw new ConflictException("A workflow must keep at least one status.");
        }

        if (moveTasksToStatusId == Guid.Empty)
        {
            throw new ValidationAppException("A replacement status is required so the removed status's tasks are not stranded.");
        }

        if (moveTasksToStatusId == statusId)
        {
            throw new ValidationAppException("The replacement status must differ from the status being removed.");
        }

        var target = scheme.Statuses.FirstOrDefault(s => s.Id == moveTasksToStatusId)
            ?? throw new ValidationAppException("The replacement status must belong to this scheme.");

        await RemapTasksAsync(statusId, target, ct);

        // ponytail: SavedView filter JSON ("status" conditions) and Automations rule config (toStatusId)
        // can still hold the removed id — that looseness predates this change and a stale filter simply
        // matches nothing. Rewrite them here only if that ever becomes a real complaint.
        scheme.RemoveStatus(statusId);

        Audit("status_scheme.status_removed", nameof(StatusScheme), scheme.Id, new { statusId, moveTasksToStatusId });
        await SaveAsync(ct);
        return WorkMapper.ToDto(scheme);
    }

    public async Task DeleteSchemeAsync(Guid schemeId, CancellationToken ct = default)
    {
        var scheme = await LoadForManageAsync(schemeId, ct);
        if (scheme.IsDefault)
        {
            throw new ConflictException("The default workflow cannot be deleted.");
        }

        var users = await lists.ListBySchemeAsync(schemeId, ct);
        if (users.Count > 0)
        {
            throw new ConflictException($"{users.Count} {(users.Count == 1 ? "list uses" : "lists use")} this workflow.");
        }

        // A Space override loses its owner along with the scheme; the FK is ON DELETE SET NULL, but
        // clearing it here keeps the tracked graph and the database saying the same thing.
        if (scheme.SpaceId is { } spaceId && await spaces.FindAsync(spaceId, ct) is { } space)
        {
            space.SetStatusScheme(null, UserId, Now);
        }

        schemes.Remove(scheme);
        Audit("status_scheme.deleted", nameof(StatusScheme), scheme.Id);
        await SaveAsync(ct);
    }

    /// <summary>The Space's effective scheme, and whether it is the Space's own override.</summary>
    public async Task<SpaceStatusSchemeDto> GetForSpaceAsync(Guid spaceId, CancellationToken ct = default)
    {
        var space = await RequireSpaceAsync(spaceId, ct);
        await EnsureReadAsync(space, WorkResourceTypes.Space, ct);

        var scheme = await provisioning.EffectiveSchemeAsync(space, ct);
        await SaveAsync(ct);
        return new SpaceStatusSchemeDto(WorkMapper.ToDto(scheme), space.StatusSchemeId is not null);
    }

    /// <summary>
    /// Gives the Space its own scheme: a clone of its current effective scheme (lossless — CloneFor's
    /// id map moves every task to the matching status), or a fresh scheme built from
    /// <paramref name="presetStatuses"/>. A preset has no id map to follow, so those tasks all land on the
    /// new scheme's DefaultStatus() — the caller picked a different workflow, so there is nothing to match.
    /// Idempotent: a Space that already has an override gets it back unchanged.
    /// </summary>
    public async Task<SpaceStatusSchemeDto> CustomizeSpaceAsync(
        Guid spaceId,
        IReadOnlyList<(string Name, StatusCategory Category, string? Color)>? presetStatuses,
        CancellationToken ct = default)
    {
        var space = await RequireSpaceAsync(spaceId, ct);
        await EnsureManageStructureAsync(space, WorkResourceTypes.Space, ct);

        if (space.StatusSchemeId is { } existingId)
        {
            var existing = await schemes.FindAsync(existingId, ct)
                ?? throw new NotFoundException("Status scheme not found.");
            return new SpaceStatusSchemeDto(WorkMapper.ToDto(existing), true);
        }

        var source = await provisioning.EffectiveSchemeAsync(space, ct);

        StatusScheme clone;
        IReadOnlyDictionary<Guid, Guid>? map;
        if (presetStatuses is { Count: > 0 })
        {
            clone = StatusScheme.CreateForSpace(NewId(), space.WorkspaceId, space.Id, space.Name);
            double position = Positioning.Step;
            foreach (var s in presetStatuses)
            {
                clone.AddStatus(NewId(), s.Name, s.Category, string.IsNullOrWhiteSpace(s.Color) ? "#8b8b8b" : s.Color!, position);
                position += Positioning.Step;
            }

            map = null;
        }
        else
        {
            (clone, map) = source.CloneFor(NewId(), space.Id, NewId);
        }

        schemes.Add(clone);
        space.SetStatusScheme(clone.Id, UserId, Now);

        // Scoped per list, not per status: the source scheme is still in use by every OTHER Space, so
        // remapping by status id would drag their tasks onto this Space's clone too.
        var fallback = clone.DefaultStatus();
        foreach (var list in await lists.ListBySpaceAsync(space.Id, ct))
        {
            if (list.StatusSchemeId != source.Id)
            {
                continue;
            }

            list.SetStatusScheme(clone.Id, UserId, Now);

            // ponytail: per-task loop; batch if a single status ever holds 10k+ tasks.
            foreach (var task in await tasks.ListByListAsync(list.Id, ct))
            {
                // ListByListAsync is membership-driven, not filtered by WorkItem.ListId, so it also
                // returns tasks merely ADDED to this list. Those keep their primary list's scheme —
                // moving them here would leave their status foreign to their own list's workflow.
                if (task.ListId != list.Id)
                {
                    continue;
                }

                var target = map is not null && map.TryGetValue(task.StatusId, out var mapped)
                    ? clone.Statuses.First(s => s.Id == mapped)
                    : fallback;
                task.ChangeStatus(target.Id, target.IsCompletedCategory, UserId, Now);
            }
        }

        Audit("space.status_scheme_customized", nameof(Space), space.Id, new { schemeId = clone.Id });
        await SaveAsync(ct);
        return new SpaceStatusSchemeDto(WorkMapper.ToDto(clone), true);
    }

    /// <summary>
    /// Drops the Space's override and puts its lists back on the workspace default, moving every task
    /// through <paramref name="mapping"/>. Every status of the Space scheme that still holds tasks needs a
    /// mapping entry — the caller decides where those tasks land, this never guesses.
    /// </summary>
    public async Task<SpaceStatusSchemeDto> ResetSpaceAsync(
        Guid spaceId, IReadOnlyList<StatusMappingInput> mapping, CancellationToken ct = default)
    {
        var space = await RequireSpaceAsync(spaceId, ct);
        await EnsureManageStructureAsync(space, WorkResourceTypes.Space, ct);

        var workspaceDefault = await provisioning.EnsureDefaultSchemeAsync(space.WorkspaceId, ct);
        if (space.StatusSchemeId is not { } spaceSchemeId)
        {
            return new SpaceStatusSchemeDto(WorkMapper.ToDto(workspaceDefault), false);
        }

        var spaceScheme = await schemes.FindAsync(spaceSchemeId, ct)
            ?? throw new NotFoundException("Status scheme not found.");

        var missing = new List<string>();
        foreach (var status in spaceScheme.Statuses)
        {
            if (mapping.All(m => m.FromStatusId != status.Id) && await tasks.CountByStatusAsync(status.Id, ct) > 0)
            {
                missing.Add(status.Name);
            }
        }

        if (missing.Count > 0)
        {
            throw new ValidationAppException(
                $"A replacement status is required for: {string.Join(", ", missing)}.");
        }

        foreach (var entry in mapping)
        {
            // Without this, a caller could name any status in the workspace and drag unrelated Spaces'
            // tasks onto a workspace-default status.
            if (spaceScheme.Statuses.All(s => s.Id != entry.FromStatusId))
            {
                throw new ValidationAppException("The status being replaced must belong to this space's workflow.");
            }

            var target = workspaceDefault.Statuses.FirstOrDefault(s => s.Id == entry.ToStatusId)
                ?? throw new ValidationAppException("The replacement status must belong to the workspace default workflow.");
            await RemapTasksAsync(entry.FromStatusId, target, ct);
        }

        foreach (var list in await lists.ListBySchemeAsync(spaceScheme.Id, ct))
        {
            list.SetStatusScheme(workspaceDefault.Id, UserId, Now);
        }

        space.SetStatusScheme(null, UserId, Now);
        schemes.Remove(spaceScheme);

        Audit("space.status_scheme_reset", nameof(Space), space.Id, new { removedSchemeId = spaceScheme.Id });
        await SaveAsync(ct);
        return new SpaceStatusSchemeDto(WorkMapper.ToDto(workspaceDefault), false);
    }

    /// <summary>Moves every task on <paramref name="fromStatusId"/> onto <paramref name="target"/> through
    /// WorkItem.ChangeStatus, so IsCompleted/CompletedAtUtc and the status-changed event stay correct.</summary>
    private async Task RemapTasksAsync(Guid fromStatusId, StatusDefinition target, CancellationToken ct)
    {
        // ponytail: per-task loop; batch if a single status ever holds 10k+ tasks.
        foreach (var task in await tasks.ListByStatusAsync(fromStatusId, ct))
        {
            task.ChangeStatus(target.Id, target.IsCompletedCategory, UserId, Now);
        }
    }

    private async Task<StatusScheme> LoadForManageAsync(Guid schemeId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureManageStructure((await AccessAsync(workspaceId, ct))?.Role);

        var scheme = await schemes.FindAsync(schemeId, ct);
        if (scheme is null || scheme.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Status scheme not found.");
        }

        return scheme;
    }

    private async Task<Space> RequireSpaceAsync(Guid spaceId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var space = await spaces.FindAsync(spaceId, ct);
        if (space is null || space.IsDeleted || space.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Space not found.");
        }

        return space;
    }
}
