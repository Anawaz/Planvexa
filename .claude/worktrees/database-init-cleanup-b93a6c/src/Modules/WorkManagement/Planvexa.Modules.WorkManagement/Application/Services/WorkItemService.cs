namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Workspaces;

public sealed class TagService(WorkServiceContext ctx, ITagStore tags) : WorkServiceBase(ctx)
{
    public async Task<IReadOnlyList<TagDto>> ListAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);
        var list = await tags.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(WorkMapper.ToDto).ToList();
    }

    public async Task<TagDto> CreateAsync(string name, string? color, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureEditContent((await AccessAsync(workspaceId, ct))?.Role);
        var tag = Tag.Create(NewId(), workspaceId, name, color);
        tags.Add(tag);
        Audit("tag.created", nameof(Tag), tag.Id, new { name });
        await SaveAsync(ct);
        return WorkMapper.ToDto(tag);
    }
}

public sealed class WorkItemService(
    WorkServiceContext ctx,
    ITaskListStore lists,
    IStatusSchemeStore schemes,
    ITagStore tags,
    IWorkItemStore tasks,
    IDependencyStore dependencies,
    IChecklistStore checklists,
    ICustomFieldStore customFields,
    IActivityStore activity,
    ITaskListMembershipStore memberships,
    ITaskRelationStore relations,
    IAttachmentStore attachments,
    CustomFieldService customFieldService) : WorkServiceBase(ctx)
{
    public async Task<TaskDto> CreateAsync(CreateTaskCommand command, string? idempotencyKey = null, CancellationToken ct = default)
    {
        var list = await lists.FindAsync(command.ListId, ct);
        if (list is null || list.IsDeleted)
        {
            throw new NotFoundException("List not found.");
        }

        await EnsureEditContentAsync(list, WorkResourceTypes.List, ct);

        // Offline-mutation-outbox replay guard: a repeated create with the same Idempotency-Key returns
        // the original task instead of inserting a duplicate (see WorkItem.IdempotencyKey's doc comment).
        var key = idempotencyKey?.Trim();
        if (!string.IsNullOrEmpty(key))
        {
            var existing = await tasks.FindByIdempotencyKeyAsync(list.WorkspaceId, key, ct);
            if (existing is not null)
            {
                return WorkMapper.ToDto(existing);
            }
        }

        // Resolve status: requested, else the scheme default.
        StatusDefinition status;
        if (command.StatusId is { } requestedStatusId)
        {
            status = await RequireStatusInSchemeAsync(list.StatusSchemeId, requestedStatusId, ct);
        }
        else
        {
            var scheme = await schemes.FindAsync(list.StatusSchemeId, ct)
                ?? throw new NotFoundException("The list's status scheme is missing.");
            status = scheme.DefaultStatus();
        }

        if (command.ParentId is { } parentId)
        {
            var parent = await tasks.FindAsync(parentId, ct)
                ?? throw new NotFoundException("Parent task not found.");
            if (parent.ListId != list.Id)
            {
                throw new ValidationAppException("A subtask must belong to the same list as its parent.");
            }
        }

        var sequence = list.NextTaskSequence();
        var maxPos = await tasks.MaxPositionAsync(list.Id, ct);
        var position = Positioning.Append(maxPos);
        var task = WorkItem.Create(
            NewId(), list.WorkspaceId, list.SpaceId, list.Id, command.ParentId,
            sequence, command.Title, status.Id, status.IsCompletedCategory, position, UserId, Now, key);

        task.UpdateDetails(null, command.Description, command.Priority, command.StartDate, command.DueDate, command.IsMilestone, UserId, Now);

        if (command.TaskTypeId is { } taskTypeId)
        {
            task.SetTaskType(taskTypeId, UserId, Now);
        }

        if (command.CustomId is not null)
        {
            if (await tasks.CustomIdExistsAsync(list.Id, command.CustomId, excludeTaskId: null, ct))
            {
                throw new ValidationAppException($"Custom id '{command.CustomId}' is already used in this list.");
            }

            task.SetCustomId(command.CustomId, UserId, Now);
        }

        foreach (var assignee in command.AssigneeUserIds ?? [])
        {
            task.AddAssignee(NewId(), assignee, UserId, Now);
        }

        if (command.TagIds is { Count: > 0 } tagIds)
        {
            var valid = await tags.ExistingTagIdsAsync(list.WorkspaceId, tagIds, ct);
            task.SetTags(valid.ToList(), NewId, UserId, Now);
        }

        tasks.Add(task);
        memberships.Add(new TaskListMembership(NewId(), list.WorkspaceId, task.Id, list.Id, isPrimary: true, position, Now));
        activity.Add(new TaskActivityEvent(NewId(), list.WorkspaceId, task.Id, UserId, "created", null, Now));
        Audit("task.created", "Task", task.Id, new { command.Title, listId = list.Id });
        await SaveAsync(ct);
        return WorkMapper.ToDto(task);
    }

    /// <summary>
    /// Creates a copy of a task in the same list: title suffixed "(Copy)", plus description, priority,
    /// dates, milestone, status, assignees, watchers, tags, checklists (and items), and custom-field
    /// values. Dependencies, attachments and nested subtasks are intentionally not copied.
    /// </summary>
    public async Task<TaskDto> DuplicateAsync(Guid taskId, CancellationToken ct = default)
    {
        var source = await tasks.FindWithRelationsAsync(taskId, ct);
        if (source is null || source.IsDeleted)
        {
            throw new NotFoundException("Task not found.");
        }

        var list = await lists.FindAsync(source.ListId, ct)
            ?? throw new NotFoundException("List not found.");
        await EnsureEditContentAsync(list, WorkResourceTypes.List, ct);

        var status = await schemes.FindStatusAsync(source.StatusId, ct)
            ?? throw new NotFoundException("The task's status is missing.");

        var sequence = list.NextTaskSequence();
        var maxPos = await tasks.MaxPositionAsync(list.Id, ct);
        var position = Positioning.Append(maxPos);
        var copy = WorkItem.Create(
            NewId(), list.WorkspaceId, source.SpaceId, list.Id, source.ParentId,
            sequence, $"{source.Title} (Copy)", status.Id, status.IsCompletedCategory, position, UserId, Now);

        copy.UpdateDetails(null, source.Description, source.Priority, source.StartDate, source.DueDate, source.IsMilestone, UserId, Now);
        // CustomId is intentionally NOT copied (it must stay unique per list); TaskType is.
        if (source.TaskTypeId is { } sourceType)
        {
            copy.SetTaskType(sourceType, UserId, Now);
        }

        foreach (var assignee in source.Assignees)
        {
            copy.AddAssignee(NewId(), assignee.UserId, UserId, Now);
        }

        foreach (var watcher in source.Watchers)
        {
            copy.AddWatcher(NewId(), watcher.UserId, Now);
        }

        if (source.Tags.Count > 0)
        {
            copy.SetTags(source.Tags.Select(t => t.TagId).ToList(), NewId, UserId, Now);
        }

        tasks.Add(copy);
        memberships.Add(new TaskListMembership(NewId(), list.WorkspaceId, copy.Id, list.Id, isPrimary: true, position, Now));

        foreach (var sourceChecklist in await checklists.ListForTaskAsync(source.Id, ct))
        {
            var newChecklist = TaskChecklist.Create(NewId(), copy.Id, sourceChecklist.Name, sourceChecklist.Position);
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

        foreach (var value in await customFields.ListValuesForTaskAsync(source.Id, ct))
        {
            var newValue = CustomFieldValue.Create(NewId(), copy.Id, value.DefinitionId);
            if (value.OptionId is not null) newValue.SetOption(value.OptionId, Now);
            else if (value.JsonValue is not null) newValue.SetMultiSelect(value.JsonValue, Now);
            else if (value.NumberValue is not null) newValue.SetNumber(value.NumberValue, Now);
            else if (value.DateValue is not null) newValue.SetDate(value.DateValue, Now);
            else if (value.BoolValue is not null) newValue.SetBool(value.BoolValue, Now);
            else newValue.SetText(value.TextValue, Now);
            customFields.AddValue(newValue);
        }

        activity.Add(new TaskActivityEvent(NewId(), list.WorkspaceId, copy.Id, UserId, "created", null, Now));
        Audit("task.duplicated", "Task", copy.Id, new { sourceTaskId = source.Id, listId = list.Id });
        await SaveAsync(ct);
        return WorkMapper.ToDto(copy);
    }

    /// <summary>
    /// Cross-list copy — same depth/fields as <see cref="DuplicateAsync"/> (title suffixed
    /// "(Copy)", description, priority, dates, milestone, type, assignees, watchers, tags,
    /// checklists, custom-field values; dependencies/attachments/subtasks not copied) but placed into a
    /// DIFFERENT target List via a fresh primary <see cref="TaskListMembership"/>, unlike Duplicate which
    /// only ever copies within the same list. If the target list uses a different status scheme than the
    /// source, the copy falls back to the target list's default status (the source status id would not
    /// exist there).
    /// </summary>
    public async Task<TaskDto> CopyToListAsync(Guid taskId, Guid targetListId, CancellationToken ct = default)
    {
        var source = await tasks.FindWithRelationsAsync(taskId, ct);
        if (source is null || source.IsDeleted)
        {
            throw new NotFoundException("Task not found.");
        }

        await EnsureReadAsync(source, WorkResourceTypes.Task, ct);

        var targetList = await lists.FindAsync(targetListId, ct);
        if (targetList is null || targetList.IsDeleted)
        {
            throw new NotFoundException("Target list not found.");
        }

        await EnsureEditContentAsync(targetList, WorkResourceTypes.List, ct);

        var sourceStatus = await schemes.FindStatusAsync(source.StatusId, ct)
            ?? throw new NotFoundException("The task's status is missing.");

        StatusDefinition targetStatus;
        if (sourceStatus.SchemeId == targetList.StatusSchemeId)
        {
            targetStatus = sourceStatus;
        }
        else
        {
            var targetScheme = await schemes.FindAsync(targetList.StatusSchemeId, ct)
                ?? throw new NotFoundException("The target list's status scheme is missing.");
            targetStatus = targetScheme.DefaultStatus();
        }

        var sequence = targetList.NextTaskSequence();
        var maxPos = await tasks.MaxPositionAsync(targetList.Id, ct);
        var position = Positioning.Append(maxPos);
        var copy = WorkItem.Create(
            NewId(), targetList.WorkspaceId, targetList.SpaceId, targetList.Id, parentId: null,
            sequence, $"{source.Title} (Copy)", targetStatus.Id, targetStatus.IsCompletedCategory, position, UserId, Now);

        copy.UpdateDetails(null, source.Description, source.Priority, source.StartDate, source.DueDate, source.IsMilestone, UserId, Now);
        if (source.TaskTypeId is { } sourceType)
        {
            copy.SetTaskType(sourceType, UserId, Now);
        }

        foreach (var assignee in source.Assignees)
        {
            copy.AddAssignee(NewId(), assignee.UserId, UserId, Now);
        }

        foreach (var watcher in source.Watchers)
        {
            copy.AddWatcher(NewId(), watcher.UserId, Now);
        }

        if (source.Tags.Count > 0)
        {
            copy.SetTags(source.Tags.Select(t => t.TagId).ToList(), NewId, UserId, Now);
        }

        tasks.Add(copy);
        memberships.Add(new TaskListMembership(NewId(), targetList.WorkspaceId, copy.Id, targetList.Id, isPrimary: true, position, Now));

        foreach (var sourceChecklist in await checklists.ListForTaskAsync(source.Id, ct))
        {
            var newChecklist = TaskChecklist.Create(NewId(), copy.Id, sourceChecklist.Name, sourceChecklist.Position);
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

        foreach (var value in await customFields.ListValuesForTaskAsync(source.Id, ct))
        {
            var newValue = CustomFieldValue.Create(NewId(), copy.Id, value.DefinitionId);
            if (value.OptionId is not null) newValue.SetOption(value.OptionId, Now);
            else if (value.JsonValue is not null) newValue.SetMultiSelect(value.JsonValue, Now);
            else if (value.NumberValue is not null) newValue.SetNumber(value.NumberValue, Now);
            else if (value.DateValue is not null) newValue.SetDate(value.DateValue, Now);
            else if (value.BoolValue is not null) newValue.SetBool(value.BoolValue, Now);
            else newValue.SetText(value.TextValue, Now);
            customFields.AddValue(newValue);
        }

        activity.Add(new TaskActivityEvent(NewId(), targetList.WorkspaceId, copy.Id, UserId, "created", null, Now));
        Audit("task.copied", "Task", copy.Id, new { sourceTaskId = source.Id, targetListId = targetList.Id });
        await SaveAsync(ct);
        return WorkMapper.ToDto(copy);
    }

    /// <summary>
    /// Moves the source task's checklists, attachments, and any custom-field values the
    /// target does not already have set onto the target task, then archives (soft-deletes) the source.
    /// Comments (Collaboration module) and time entries (TimeTracking module) are NOT moved here
    /// — they are cross-module data and would need each module to react to a domain event to reassign
    /// their own rows; this change does not wire that up (documented gap). The target
    /// gets an activity entry recording the merge.
    /// </summary>
    public async Task<TaskDto> MergeAsync(Guid sourceTaskId, Guid targetTaskId, CancellationToken ct = default)
    {
        if (sourceTaskId == targetTaskId)
        {
            throw new ValidationAppException("Cannot merge a task into itself.");
        }

        var source = await tasks.FindWithRelationsAsync(sourceTaskId, ct);
        if (source is null || source.IsDeleted)
        {
            throw new NotFoundException("Source task not found.");
        }

        var target = await tasks.FindWithRelationsAsync(targetTaskId, ct);
        if (target is null || target.IsDeleted)
        {
            throw new NotFoundException("Target task not found.");
        }

        await EnsureEditContentAsync(source, WorkResourceTypes.Task, ct);
        await EnsureEditContentAsync(target, WorkResourceTypes.Task, ct);

        if (source.WorkspaceId != target.WorkspaceId)
        {
            throw new ValidationAppException("Cannot merge tasks across workspaces.");
        }

        foreach (var checklist in await checklists.ListForTaskAsync(source.Id, ct))
        {
            checklist.ReassignTask(target.Id);
        }

        foreach (var attachment in await attachments.ListForTaskAsync(source.Id, ct))
        {
            attachment.ReassignTask(target.Id);
        }

        foreach (var value in await customFields.ListValuesForTaskAsync(source.Id, ct))
        {
            var targetAlreadyHasValue = await customFields.FindValueAsync(target.Id, value.DefinitionId, ct) is not null;
            if (!targetAlreadyHasValue)
            {
                value.ReassignTask(target.Id);
            }
        }

        activity.Add(new TaskActivityEvent(NewId(), target.WorkspaceId, target.Id, UserId, "merged_from", source.Title, Now));
        source.SoftDelete(UserId, Now);
        Audit("task.merged", "Task", target.Id, new { sourceTaskId = source.Id });
        await SaveAsync(ct);
        return WorkMapper.ToDto(target);
    }

    public async Task<TaskDetailDto> GetAsync(Guid taskId, CancellationToken ct = default)
    {
        var (task, _) = await LoadForReadAsync(taskId, ct);

        var checklistDtos = (await checklists.ListForTaskAsync(task.Id, ct)).Select(WorkMapper.ToDto).ToList();
        var dependencyDtos = (await dependencies.ListForTaskAsync(task.Id, ct)).Select(WorkMapper.ToDto).ToList();

        // Effective values, including read-time-computed Formula/Rollup fields — not the
        // raw stored rows, so a Formula/Rollup field's value/error always reflects the task's current data.
        var valueDtos = await customFieldService.ListEffectiveValuesForTaskAsync(task, ct);
        var activityDtos = (await activity.ListForTaskAsync(task.Id, 100, ct)).Select(WorkMapper.ToDto).ToList();
        var listDtos = (await memberships.ListForTaskAsync(task.Id, ct)).Select(WorkMapper.ToDto).ToList();
        var relationDtos = (await relations.ListForTaskAsync(task.Id, ct)).Select(r => WorkMapper.ToRelationDto(r, task.Id)).ToList();

        return new TaskDetailDto(
            WorkMapper.ToDto(task),
            task.Watchers.Select(w => w.UserId).ToList(),
            checklistDtos, dependencyDtos, valueDtos, activityDtos, listDtos, relationDtos);
    }

    /// <summary>Every List a task belongs to (not just its primary list).</summary>
    public async Task<IReadOnlyList<TaskListMembershipDto>> ListMembershipsAsync(Guid taskId, CancellationToken ct = default)
    {
        var (task, _) = await LoadForReadAsync(taskId, ct);
        return (await memberships.ListForTaskAsync(task.Id, ct)).Select(WorkMapper.ToDto).ToList();
    }

    /// <summary>Adds the task to another List without removing it from any existing one (unlike
    /// <see cref="MoveAsync"/>, which changes the PRIMARY list and removes the old one).</summary>
    public async Task<IReadOnlyList<TaskListMembershipDto>> AddToListAsync(Guid taskId, Guid targetListId, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(taskId, ct);
        if (task is null || task.IsDeleted)
        {
            throw new NotFoundException("Task not found.");
        }

        await EnsureEditContentAsync(task, WorkResourceTypes.Task, ct);

        var targetList = await lists.FindAsync(targetListId, ct);
        if (targetList is null || targetList.IsDeleted)
        {
            throw new NotFoundException("List not found.");
        }

        await EnsureEditContentAsync(targetList, WorkResourceTypes.List, ct);

        if (await memberships.FindAsync(taskId, targetListId, ct) is null)
        {
            var maxPos = await tasks.MaxPositionAsync(targetListId, ct);
            memberships.Add(new TaskListMembership(NewId(), task.WorkspaceId, task.Id, targetListId, isPrimary: false, Positioning.Append(maxPos), Now));
            activity.Add(new TaskActivityEvent(NewId(), task.WorkspaceId, task.Id, UserId, "list_added", targetListId.ToString(), Now));
            Audit("task.list_added", "Task", task.Id, new { listId = targetListId });
            await SaveAsync(ct);
        }

        return (await memberships.ListForTaskAsync(taskId, ct)).Select(WorkMapper.ToDto).ToList();
    }

    /// <summary>Removes the task from a non-primary List. The primary list cannot be removed this
    /// way — use <see cref="MoveAsync"/> to change which list is primary first.</summary>
    public async Task<IReadOnlyList<TaskListMembershipDto>> RemoveFromListAsync(Guid taskId, Guid listId, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(taskId, ct);
        if (task is null || task.IsDeleted)
        {
            throw new NotFoundException("Task not found.");
        }

        await EnsureEditContentAsync(task, WorkResourceTypes.Task, ct);

        var existing = await memberships.FindAsync(taskId, listId, ct);
        if (existing is not null)
        {
            if (existing.IsPrimary)
            {
                throw new ValidationAppException("Cannot remove a task from its primary list; move it to another list first.");
            }

            memberships.Remove(existing);
            activity.Add(new TaskActivityEvent(NewId(), task.WorkspaceId, task.Id, UserId, "list_removed", listId.ToString(), Now));
            Audit("task.list_removed", "Task", task.Id, new { listId });
            await SaveAsync(ct);
        }

        return (await memberships.ListForTaskAsync(taskId, ct)).Select(WorkMapper.ToDto).ToList();
    }

    public async Task<TaskRelationDto> AddRelationAsync(Guid taskId, Guid relatedTaskId, CancellationToken ct = default)
    {
        if (taskId == relatedTaskId)
        {
            throw new ValidationAppException("A task cannot relate to itself.");
        }

        var (task, _) = await LoadForEditAsync(taskId, ct);
        var related = await tasks.FindAsync(relatedTaskId, ct) ?? throw new NotFoundException("Related task not found.");
        await EnsureReadAsync(related, WorkResourceTypes.Task, ct);

        var existing = await relations.FindAsync(taskId, relatedTaskId, ct);
        if (existing is null)
        {
            existing = new TaskRelation(NewId(), task.Id, relatedTaskId, Now);
            relations.Add(existing);
            Audit("task.relation_added", "Task", task.Id, new { relatedTaskId });
            await SaveAsync(ct);
        }

        return WorkMapper.ToRelationDto(existing, task.Id);
    }

    public async Task RemoveRelationAsync(Guid taskId, Guid relatedTaskId, CancellationToken ct = default)
    {
        var (task, _) = await LoadForEditAsync(taskId, ct);
        var existing = await relations.FindAsync(taskId, relatedTaskId, ct);
        if (existing is not null)
        {
            relations.Remove(existing);
            Audit("task.relation_removed", "Task", task.Id, new { relatedTaskId });
            await SaveAsync(ct);
        }
    }

    public async Task<TaskDto> AddTeamAssigneeAsync(Guid taskId, Guid teamId, CancellationToken ct = default)
    {
        var (task, _) = await LoadForEditWithRelationsAsync(taskId, ct);
        if (task.AddTeamAssignee(NewId(), teamId, UserId, Now))
        {
            activity.Add(new TaskActivityEvent(NewId(), task.WorkspaceId, task.Id, UserId, "team_assigned", teamId.ToString(), Now));
            Audit("task.team_assignee_added", "Task", task.Id, new { teamId });
            await SaveAsync(ct);
        }

        return WorkMapper.ToDto(task);
    }

    public async Task<TaskDto> RemoveTeamAssigneeAsync(Guid taskId, Guid teamId, CancellationToken ct = default)
    {
        var (task, _) = await LoadForEditWithRelationsAsync(taskId, ct);
        if (task.RemoveTeamAssignee(teamId, UserId, Now))
        {
            Audit("task.team_assignee_removed", "Task", task.Id, new { teamId });
            await SaveAsync(ct);
        }

        return WorkMapper.ToDto(task);
    }

    public async Task<IReadOnlyList<TaskDto>> ListByListAsync(Guid listId, CancellationToken ct = default)
    {
        var list = await lists.FindAsync(listId, ct) ?? throw new NotFoundException("List not found.");
        await EnsureReadAsync(list, WorkResourceTypes.List, ct);

        var result = await tasks.ListByListAsync(listId, ct);
        var visible = new List<TaskDto>();
        foreach (var task in result.Where(t => !t.IsDeleted))
        {
            // Evaluated THROUGH this specific list membership, not the task's single primary
            // list — a task also present in a private list must not be hidden from a public list's view,
            // and vice versa (see WorkManagementAuthorizer.EnsureReadInListContextAsync's doc comment).
            if (await CanReadInListContextAsync(task, listId, ct))
            {
                visible.Add(WorkMapper.ToDto(task));
            }
        }

        return visible;
    }

    /// <summary>Same ACL-filtered list as <see cref="ListByListAsync"/>, additionally narrowed by
    /// a nested AND/OR filter tree (see TaskFilterEvaluator). Filtering happens AFTER the ACL filter, so
    /// a filter can never surface a task the caller couldn't otherwise read.</summary>
    public async Task<IReadOnlyList<TaskDto>> QueryByListAsync(Guid listId, FilterGroupDto? filter, CancellationToken ct = default)
    {
        var visible = await ListByListAsync(listId, ct);
        return filter is null ? visible : visible.Where(t => TaskFilterEvaluator.Matches(t, filter)).ToList();
    }

    /// <summary>Gantt baselines: snapshots the task's current Start/DueDate as its baseline.</summary>
    public async Task<TaskDto> SetBaselineAsync(Guid taskId, CancellationToken ct = default)
    {
        var (task, _) = await LoadForEditAsync(taskId, ct);
        task.SetBaseline(UserId, Now);
        Audit("task.baseline_set", "Task", task.Id, new { task.StartDate, task.DueDate });
        await SaveAsync(ct);
        return WorkMapper.ToDto(task);
    }

    public async Task<IReadOnlyList<TaskDto>> ListMineAsync(CancellationToken ct = default)
    {
        var result = await tasks.ListAssignedToUserAsync(UserId, ct);
        return result.Where(t => !t.IsDeleted).OrderBy(t => t.DueDate ?? DateTimeOffset.MaxValue).Select(WorkMapper.ToDto).ToList();
    }

    public async Task<TaskDto> UpdateAsync(Guid taskId, UpdateTaskCommand command, CancellationToken ct = default)
    {
        var (task, _) = await LoadForEditAsync(taskId, ct);

        if (command.StatusId is { } statusId && statusId != task.StatusId)
        {
            var list = await lists.FindAsync(task.ListId, ct)!;
            var status = await RequireStatusInSchemeAsync(list!.StatusSchemeId, statusId, ct);
            if (status.IsCompletedCategory)
            {
                await GuardCompletionAsync(task, ct);
            }

            task.ChangeStatus(status.Id, status.IsCompletedCategory, UserId, Now);
            activity.Add(new TaskActivityEvent(NewId(), task.WorkspaceId, task.Id, UserId, "status_changed", status.Name, Now));
        }

        task.UpdateDetails(command.Title, command.Description, command.Priority, command.StartDate, command.DueDate, command.IsMilestone, UserId, Now);
        if (command.Position.HasValue)
        {
            task.Reposition(command.Position.Value);
        }

        if (command.TaskTypeId is not null)
        {
            task.SetTaskType(command.TaskTypeId, UserId, Now);
        }

        if (command.CustomId is not null)
        {
            if (await tasks.CustomIdExistsAsync(task.ListId, command.CustomId, task.Id, ct))
            {
                throw new ValidationAppException($"Custom id '{command.CustomId}' is already used in this list.");
            }

            task.SetCustomId(command.CustomId, UserId, Now);
        }

        Audit("task.updated", "Task", task.Id);
        await SaveAsync(ct);
        await NotifyRealtimeAsync(task.WorkspaceId, task.Id, "updated", ct);
        return WorkMapper.ToDto(task);
    }

    /// <summary>
    /// Classic single-primary-list move: the task LEAVES its old primary list and joins
    /// <c>command.ListId</c> as the new primary (any OTHER non-primary memberships added via
    /// <see cref="AddToListAsync"/> are left untouched). This is deliberately different from
    /// AddToListAsync/RemoveFromListAsync, which grow/shrink the multi-list membership set without
    /// touching which list is primary.
    /// </summary>
    public async Task<TaskDto> MoveAsync(Guid taskId, MoveTaskCommand command, CancellationToken ct = default)
    {
        var (task, _) = await LoadForEditAsync(taskId, ct);

        var targetList = command.ListId is { } newListId && newListId != task.ListId
            ? await lists.FindAsync(newListId, ct) ?? throw new NotFoundException("Target list not found.")
            : await lists.FindAsync(task.ListId, ct);

        if (targetList is null)
        {
            throw new NotFoundException("List not found.");
        }

        await EnsureEditContentAsync(targetList, WorkResourceTypes.List, ct);

        var position = command.Position ?? Positioning.Append(await tasks.MaxPositionAsync(targetList.Id, ct));
        var oldListId = task.ListId;
        task.MoveTo(targetList.Id, targetList.SpaceId, position, UserId, Now);
        await SyncPrimaryMembershipAsync(task, oldListId, targetList.Id, position, ct);

        if (command.StatusId is { } statusId)
        {
            var status = await RequireStatusInSchemeAsync(targetList.StatusSchemeId, statusId, ct);
            if (status.IsCompletedCategory)
            {
                await GuardCompletionAsync(task, ct);
            }

            task.ChangeStatus(status.Id, status.IsCompletedCategory, UserId, Now);
        }

        activity.Add(new TaskActivityEvent(NewId(), task.WorkspaceId, task.Id, UserId, "moved", null, Now));
        Audit("task.moved", "Task", task.Id, new { targetList = targetList.Id });
        await SaveAsync(ct);
        await NotifyRealtimeAsync(task.WorkspaceId, task.Id, "moved", ct);
        return WorkMapper.ToDto(task);
    }

    public async Task<TaskDto> CompleteAsync(Guid taskId, CancellationToken ct = default)
    {
        var (task, list) = await LoadForEditAsync(taskId, ct);
        var scheme = await schemes.FindAsync(list.StatusSchemeId, ct)
            ?? throw new NotFoundException("Status scheme missing.");
        var doneStatus = scheme.Statuses.OrderByDescending(s => s.Position).FirstOrDefault(s => s.IsCompletedCategory)
            ?? throw new ValidationAppException("This list has no completed status to move the task to.");

        var blockers = await dependencies.IncompleteBlockersAsync(task.Id, ct);
        task.Complete(doneStatus.Id, blockers.Count > 0, UserId, Now);

        activity.Add(new TaskActivityEvent(NewId(), task.WorkspaceId, task.Id, UserId, "completed", null, Now));
        Audit("task.completed", "Task", task.Id);
        await SaveAsync(ct);
        await NotifyRealtimeAsync(task.WorkspaceId, task.Id, "completed", ct);
        return WorkMapper.ToDto(task);
    }

    public async Task<TaskDto> ReopenAsync(Guid taskId, CancellationToken ct = default)
    {
        var (task, list) = await LoadForEditAsync(taskId, ct);
        var scheme = await schemes.FindAsync(list.StatusSchemeId, ct)
            ?? throw new NotFoundException("Status scheme missing.");
        var openStatus = scheme.DefaultStatus();
        task.Reopen(openStatus.Id, UserId, Now);
        activity.Add(new TaskActivityEvent(NewId(), task.WorkspaceId, task.Id, UserId, "reopened", null, Now));
        Audit("task.reopened", "Task", task.Id);
        await SaveAsync(ct);
        return WorkMapper.ToDto(task);
    }

    public async Task DeleteAsync(Guid taskId, CancellationToken ct = default)
    {
        var (task, _) = await LoadForEditAsync(taskId, ct);
        task.SoftDelete(UserId, Now);
        Audit("task.deleted", "Task", task.Id);
        await SaveAsync(ct);
    }

    public async Task RestoreAsync(Guid taskId, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Task not found.");
        await EnsureEditContentAsync(task, WorkResourceTypes.Task, ct);
        task.Restore();
        Audit("task.restored", "Task", task.Id);
        await SaveAsync(ct);
    }

    public async Task<TaskDto> AddAssigneeAsync(Guid taskId, Guid assigneeUserId, CancellationToken ct = default)
    {
        var (task, _) = await LoadForEditWithRelationsAsync(taskId, ct);
        if (task.AddAssignee(NewId(), assigneeUserId, UserId, Now))
        {
            activity.Add(new TaskActivityEvent(NewId(), task.WorkspaceId, task.Id, UserId, "assigned", assigneeUserId.ToString(), Now));
            Audit("task.assignee_added", "Task", task.Id, new { assigneeUserId });
            await SaveAsync(ct);
        }

        return WorkMapper.ToDto(task);
    }

    public async Task<TaskDto> RemoveAssigneeAsync(Guid taskId, Guid assigneeUserId, CancellationToken ct = default)
    {
        var (task, _) = await LoadForEditWithRelationsAsync(taskId, ct);
        if (task.RemoveAssignee(assigneeUserId, UserId, Now))
        {
            Audit("task.assignee_removed", "Task", task.Id, new { assigneeUserId });
            await SaveAsync(ct);
        }

        return WorkMapper.ToDto(task);
    }

    public async Task AddWatcherAsync(Guid taskId, Guid watcherUserId, CancellationToken ct = default)
    {
        var (task, _) = await LoadForEditWithRelationsAsync(taskId, ct);
        if (task.AddWatcher(NewId(), watcherUserId, Now))
        {
            await SaveAsync(ct);
        }
    }

    public async Task RemoveWatcherAsync(Guid taskId, Guid watcherUserId, CancellationToken ct = default)
    {
        var (task, _) = await LoadForEditWithRelationsAsync(taskId, ct);
        if (task.RemoveWatcher(watcherUserId))
        {
            await SaveAsync(ct);
        }
    }

    public async Task<TaskDto> SetTagsAsync(Guid taskId, IReadOnlyCollection<Guid> tagIds, CancellationToken ct = default)
    {
        var (task, _) = await LoadForEditWithRelationsAsync(taskId, ct);
        var valid = await tags.ExistingTagIdsAsync(task.WorkspaceId, tagIds, ct);
        task.SetTags(valid.ToList(), NewId, UserId, Now);
        Audit("task.tags_set", "Task", task.Id);
        await SaveAsync(ct);
        return WorkMapper.ToDto(task);
    }

    public async Task<int> BulkUpdateAsync(BulkTaskUpdate command, CancellationToken ct = default)
    {
        var items = await tasks.ListByIdsAsync(command.TaskIds, ct);
        var affected = 0;

        foreach (var task in items.Where(t => !t.IsDeleted))
        {
            if (!await CanEditContentAsync(task, WorkResourceTypes.Task, ct))
            {
                continue;
            }

            if (command.StatusId is { } statusId)
            {
                var list = await lists.FindAsync(task.ListId, ct);
                if (list is not null)
                {
                    var status = await schemes.FindStatusAsync(statusId, ct);
                    if (status is not null)
                    {
                        if (!status.IsCompletedCategory || (await dependencies.IncompleteBlockersAsync(task.Id, ct)).Count == 0)
                        {
                            task.ChangeStatus(status.Id, status.IsCompletedCategory, UserId, Now);
                        }
                    }
                }
            }

            if (command.AddAssigneeUserId is { } assignee)
            {
                task.AddAssignee(NewId(), assignee, UserId, Now);
            }

            if (command.DueDate is { } due)
            {
                task.UpdateDetails(null, null, null, null, due, null, UserId, Now);
            }

            affected++;
        }

        Audit("task.bulk_updated", "Task", null, new { count = affected });
        await SaveAsync(ct);
        return affected;
    }

    /// <summary>Keeps TaskListMembership in sync with WorkItem.ListId after MoveAsync changes the primary
    /// list: if the task already had a (non-primary) membership row for the new list, promote it;
    /// otherwise create one. Either way, demote/delete the old primary row so Move keeps its classic
    /// "task leaves its old list" semantics (see MoveAsync's doc comment).</summary>
    private async Task SyncPrimaryMembershipAsync(WorkItem task, Guid oldListId, Guid newListId, double position, CancellationToken ct)
    {
        if (oldListId == newListId)
        {
            var same = await memberships.FindAsync(task.Id, newListId, ct);
            same?.Reposition(position);
            return;
        }

        var oldPrimary = await memberships.FindAsync(task.Id, oldListId, ct);
        if (oldPrimary is not null)
        {
            memberships.Remove(oldPrimary);
        }

        var newMembership = await memberships.FindAsync(task.Id, newListId, ct);
        if (newMembership is not null)
        {
            newMembership.MarkPrimary(true);
            newMembership.Reposition(position);
        }
        else
        {
            memberships.Add(new TaskListMembership(NewId(), task.WorkspaceId, task.Id, newListId, isPrimary: true, position, Now));
        }
    }

    private async Task GuardCompletionAsync(WorkItem task, CancellationToken ct)
    {
        var blockers = await dependencies.IncompleteBlockersAsync(task.Id, ct);
        if (blockers.Count > 0)
        {
            throw new ConflictException("This task cannot be completed while a blocking task is still open.");
        }
    }

    private async Task<StatusDefinition> RequireStatusInSchemeAsync(Guid schemeId, Guid statusId, CancellationToken ct)
    {
        var status = await schemes.FindStatusAsync(statusId, ct);
        if (status is null || status.SchemeId != schemeId)
        {
            throw new ValidationAppException("The status does not belong to this list's status scheme.");
        }

        return status;
    }

    private async Task<(WorkItem Task, WorkspaceAccess? Access)> LoadForReadAsync(Guid taskId, CancellationToken ct)
    {
        var task = await tasks.FindWithRelationsAsync(taskId, ct);
        if (task is null || task.IsDeleted)
        {
            throw new NotFoundException("Task not found.");
        }

        await EnsureReadAsync(task, WorkResourceTypes.Task, ct);
        var access = await AccessAsync(task.WorkspaceId, ct);
        return (task, access);
    }

    private async Task<(WorkItem Task, TaskList List)> LoadForEditAsync(Guid taskId, CancellationToken ct)
    {
        var task = await tasks.FindAsync(taskId, ct);
        if (task is null || task.IsDeleted)
        {
            throw new NotFoundException("Task not found.");
        }

        await EnsureEditContentAsync(task, WorkResourceTypes.Task, ct);
        var list = await lists.FindAsync(task.ListId, ct) ?? throw new NotFoundException("List not found.");
        return (task, list);
    }

    private async Task<(WorkItem Task, WorkspaceAccess? Access)> LoadForEditWithRelationsAsync(Guid taskId, CancellationToken ct)
    {
        var task = await tasks.FindWithRelationsAsync(taskId, ct);
        if (task is null || task.IsDeleted)
        {
            throw new NotFoundException("Task not found.");
        }

        await EnsureEditContentAsync(task, WorkResourceTypes.Task, ct);
        var access = await AccessAsync(task.WorkspaceId, ct);
        return (task, access);
    }
}
