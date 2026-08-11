namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Notifications;
using Planvexa.SharedContracts.Teams;
using Planvexa.SharedContracts.Work;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Implements the cross-module <see cref="ITaskWriteApi"/> so Forms (create a task from a submission,
/// route it, map fields) and Automations (apply actions) can write tasks without depending on
/// WorkManagement internals. Runs under the ambient workspace; the actor is taken from the workspace
/// context (a system/automation actor when there is no interactive user). Every write is scoped to the
/// ambient workspace via the store's workspace query filter.
/// </summary>
public sealed class TaskWriteApi(
    IWorkspaceContextAccessor workspaceAccessor,
    IIdGenerator ids,
    IClock clock,
    ITaskListStore lists,
    IStatusSchemeStore schemes,
    ITagStore tags,
    IWorkItemStore tasks,
    ITaskListMembershipStore memberships,
    ICustomFieldStore customFields,
    IAttachmentStore attachments,
    IUnitOfWork unitOfWork,
    IWorkspaceAccessQuery access,
    ITeamDirectoryQuery teamDirectory,
    INotificationPublisher notifications) : ITaskWriteApi
{
    private Guid Actor => workspaceAccessor.Current.UserId;
    private DateTimeOffset Now => clock.UtcNow;

    public async Task<Guid?> CreateTaskAsync(Guid listId, string title, string? description, CancellationToken cancellationToken = default)
    {
        if (!workspaceAccessor.Current.HasWorkspace)
        {
            return null;
        }

        var list = await lists.FindAsync(listId, cancellationToken);
        if (list is null || list.IsDeleted)
        {
            return null;
        }

        var scheme = await schemes.FindAsync(list.StatusSchemeId, cancellationToken);
        if (scheme is null)
        {
            return null;
        }

        var status = scheme.DefaultStatus();
        var sequence = list.NextTaskSequence();
        var maxPos = await tasks.MaxPositionAsync(list.Id, cancellationToken);
        var position = Positioning.Append(maxPos);

        var task = WorkItem.Create(
            ids.NewId(), list.WorkspaceId, list.SpaceId, list.Id, parentId: null,
            sequence, title, status.Id, status.IsCompletedCategory, position, Actor, Now);

        if (!string.IsNullOrWhiteSpace(description))
        {
            task.UpdateDetails(null, description, null, null, null, null, Actor, Now);
        }

        tasks.Add(task);
        memberships.Add(new TaskListMembership(ids.NewId(), list.WorkspaceId, task.Id, list.Id, isPrimary: true, position, Now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return task.Id;
    }

    public async Task<bool> SetStatusByNameAsync(Guid taskId, string statusName, CancellationToken cancellationToken = default)
    {
        var task = await tasks.FindWithRelationsAsync(taskId, cancellationToken);
        if (task is null || task.IsDeleted)
        {
            return false;
        }

        var list = await lists.FindAsync(task.ListId, cancellationToken);
        if (list is null)
        {
            return false;
        }

        var scheme = await schemes.FindAsync(list.StatusSchemeId, cancellationToken);
        var status = scheme?.Statuses.FirstOrDefault(s => string.Equals(s.Name, statusName, StringComparison.OrdinalIgnoreCase));
        if (status is null)
        {
            return false;
        }

        var oldStatusId = task.StatusId;
        task.ChangeStatus(status.Id, status.IsCompletedCategory, Actor, Now);
        await NotifyStatusChangeAsync(task, oldStatusId, status, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Fires the "status_change" notification for a task's watchers and assignees (excluding
    /// whoever made the change). Deduplicated per task+transition+recipient by
    /// <see cref="INotificationPublisher"/> itself.</summary>
    private async Task NotifyStatusChangeAsync(WorkItem task, Guid oldStatusId, StatusDefinition newStatus, CancellationToken ct)
    {
        var recipients = task.Assignees.Select(a => a.UserId)
            .Concat(task.Watchers.Select(w => w.UserId))
            .Distinct()
            .Where(userId => userId != Actor);

        foreach (var userId in recipients)
        {
            await notifications.PublishAsync(new NotificationRequest(
                RecipientUserId: userId,
                EventType: "status_change",
                EntityType: "Task",
                EntityId: task.Id,
                WorkspaceId: task.WorkspaceId,
                DeduplicationKey: $"status_change:{task.Id:N}:{oldStatusId:N}:{newStatus.Id:N}:{userId:N}",
                Payload: new Dictionary<string, string>
                {
                    ["taskTitle"] = task.Title,
                    ["statusName"] = newStatus.Name,
                    ["byUserId"] = Actor.ToString(),
                }), ct);
        }
    }

    public async Task<bool> AssignAsync(Guid taskId, Guid userId, CancellationToken cancellationToken = default)
    {
        var task = await tasks.FindWithRelationsAsync(taskId, cancellationToken);
        if (task is null || task.IsDeleted)
        {
            return false;
        }

        if (await access.GetAccessAsync(task.WorkspaceId, userId, cancellationToken) is null)
        {
            return false;
        }

        if (task.AddAssignee(ids.NewId(), userId, Actor, Now) && userId != Actor)
        {
            await notifications.PublishAsync(new NotificationRequest(
                RecipientUserId: userId,
                EventType: "assignment",
                EntityType: "Task",
                EntityId: task.Id,
                WorkspaceId: task.WorkspaceId,
                DeduplicationKey: $"assignment:{task.Id:N}:{userId:N}",
                Payload: new Dictionary<string, string> { ["taskTitle"] = task.Title, ["byUserId"] = Actor.ToString() }), cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AddTagByNameAsync(Guid taskId, string tagName, CancellationToken cancellationToken = default)
    {
        var task = await tasks.FindWithRelationsAsync(taskId, cancellationToken);
        if (task is null || task.IsDeleted)
        {
            return false;
        }

        var workspaceTags = await tags.ListByWorkspaceAsync(task.WorkspaceId, cancellationToken);
        var tag = workspaceTags.FirstOrDefault(t => string.Equals(t.Name, tagName, StringComparison.OrdinalIgnoreCase));
        if (tag is null)
        {
            tag = Tag.Create(ids.NewId(), task.WorkspaceId, tagName, null);
            tags.Add(tag);
        }

        var tagIds = task.Tags.Select(t => t.TagId).ToHashSet();
        tagIds.Add(tag.Id);
        task.SetTags(tagIds, ids.NewId, Actor, Now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveTagByNameAsync(Guid taskId, string tagName, CancellationToken cancellationToken = default)
    {
        var task = await tasks.FindWithRelationsAsync(taskId, cancellationToken);
        if (task is null || task.IsDeleted)
        {
            return false;
        }

        var workspaceTags = await tags.ListByWorkspaceAsync(task.WorkspaceId, cancellationToken);
        var tag = workspaceTags.FirstOrDefault(t => string.Equals(t.Name, tagName, StringComparison.OrdinalIgnoreCase));
        if (tag is null || task.Tags.All(t => t.TagId != tag.Id))
        {
            return true;
        }

        var tagIds = task.Tags.Select(t => t.TagId).Where(id => id != tag.Id).ToHashSet();
        task.SetTags(tagIds, ids.NewId, Actor, Now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetPriorityByNameAsync(Guid taskId, string priorityName, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<TaskPriority>(priorityName, ignoreCase: true, out var priority))
        {
            return false;
        }

        var task = await tasks.FindAsync(taskId, cancellationToken);
        if (task is null || task.IsDeleted)
        {
            return false;
        }

        task.UpdateDetails(null, null, priority, null, null, null, Actor, Now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetDueDateAsync(Guid taskId, DateTimeOffset dueDate, CancellationToken cancellationToken = default)
    {
        var task = await tasks.FindAsync(taskId, cancellationToken);
        if (task is null || task.IsDeleted)
        {
            return false;
        }

        task.UpdateDetails(null, null, null, null, dueDate, null, Actor, Now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AssignTeamAsync(Guid taskId, Guid teamId, CancellationToken cancellationToken = default)
    {
        var task = await tasks.FindWithRelationsAsync(taskId, cancellationToken);
        if (task is null || task.IsDeleted)
        {
            return false;
        }

        if (!await teamDirectory.TeamExistsAsync(task.WorkspaceId, teamId, cancellationToken))
        {
            return false;
        }

        task.AddTeamAssignee(ids.NewId(), teamId, Actor, Now); // idempotent: false-but-harmless if already assigned
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetCustomFieldValueAsync(Guid taskId, Guid definitionId, string? rawValue, CancellationToken cancellationToken = default)
    {
        var task = await tasks.FindAsync(taskId, cancellationToken);
        if (task is null || task.IsDeleted)
        {
            return false;
        }

        var definition = await customFields.FindAsync(definitionId, cancellationToken);
        if (definition is null || definition.WorkspaceId != task.WorkspaceId)
        {
            return false;
        }

        // Computed fields (Formula/Rollup) never have a stored value; Relationship needs the dedicated
        // relationship endpoint; User needs an async workspace-membership check this system-actor write
        // path doesn't perform for an anonymous form respondent (see ITaskWriteApi's doc comment).
        if (definition.IsComputed || definition.Type is CustomFieldType.Relationship or CustomFieldType.User)
        {
            return false;
        }

        if (definition.Type == CustomFieldType.Team && !string.IsNullOrWhiteSpace(rawValue))
        {
            if (!Guid.TryParse(rawValue, out var teamId) || !await teamDirectory.TeamExistsAsync(task.WorkspaceId, teamId, cancellationToken))
            {
                return false;
            }
        }

        var value = await customFields.FindValueAsync(taskId, definitionId, cancellationToken);
        if (value is null)
        {
            value = CustomFieldValue.Create(ids.NewId(), taskId, definitionId);
            customFields.AddValue(value);
        }

        try
        {
            CustomFieldValueCoercion.Apply(definition, value, rawValue, Now);
        }
        catch (ValidationAppException)
        {
            return false;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AttachFileAsync(Guid taskId, string storagePath, string fileName, string contentType, long sizeBytes, CancellationToken cancellationToken = default)
    {
        var task = await tasks.FindAsync(taskId, cancellationToken);
        if (task is null || task.IsDeleted)
        {
            return false;
        }

        attachments.Add(new TaskAttachment(
            ids.NewId(), task.WorkspaceId, task.Id, fileName, contentType, sizeBytes, storagePath, Actor, Now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
