namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.WorkManagement.Application;
using Planvexa.Modules.WorkManagement.Domain;

internal sealed class WorkItemStore(PlanvexaDbContext db) : IWorkItemStore
{
    public void Add(WorkItem task) => db.Set<WorkItem>().Add(task);

    public Task<WorkItem?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<WorkItem>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<WorkItem?> FindWithRelationsAsync(Guid id, CancellationToken ct = default)
        => db.Set<WorkItem>()
            .Include(x => x.Assignees)
            .Include(x => x.Watchers)
            .Include(x => x.Tags)
            .Include(x => x.TeamAssignees)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<WorkItem>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        => await db.Set<WorkItem>().Include(x => x.Assignees)
            .Where(x => ids.Contains(x.Id)).ToListAsync(ct);

    // Driven by TaskListMembership, not WorkItem.ListId, so a task added to a second list shows
    // up in that list's view too. Ordered by the membership's own per-list Position.
    public async Task<IReadOnlyList<WorkItem>> ListByListAsync(Guid listId, CancellationToken ct = default)
    {
        var ordered = await db.Set<TaskListMembership>()
            .Where(m => m.ListId == listId)
            .OrderBy(m => m.Position)
            .Select(m => m.TaskId)
            .ToListAsync(ct);

        if (ordered.Count == 0)
        {
            return [];
        }

        var byId = await db.Set<WorkItem>().Include(x => x.Assignees).Include(x => x.Tags)
            .Where(x => ordered.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        return ordered.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    public async Task<IReadOnlyList<WorkItem>> ListAssignedToUserAsync(Guid userId, CancellationToken ct = default)
    {
        var taskIds = db.Set<TaskAssignee>()
            .Where(a => a.UserId == userId)
            .Select(a => a.TaskId);

        return await db.Set<WorkItem>().Include(x => x.Assignees).Include(x => x.Tags)
            .Where(x => taskIds.Contains(x.Id))
            .ToListAsync(ct);
    }

    public async Task<double?> MaxPositionAsync(Guid listId, CancellationToken ct = default)
        => await db.Set<TaskListMembership>().Where(x => x.ListId == listId)
            .Select(x => (double?)x.Position).MaxAsync(ct);

    public Task<bool> CustomIdExistsAsync(Guid listId, string customId, Guid? excludeTaskId, CancellationToken ct = default)
        => db.Set<WorkItem>().AnyAsync(x =>
            x.ListId == listId && x.CustomId == customId && (excludeTaskId == null || x.Id != excludeTaskId), ct);

    public Task<WorkItem?> FindByIdempotencyKeyAsync(Guid workspaceId, string key, CancellationToken ct = default)
        => db.Set<WorkItem>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.IdempotencyKey == key, ct);

    public async Task<IReadOnlyList<WorkItem>> ListSubtasksAsync(Guid parentId, CancellationToken ct = default)
        => await db.Set<WorkItem>().Where(x => x.ParentId == parentId && !x.IsDeleted).ToListAsync(ct);

    public async Task<IReadOnlyList<WorkItem>> ListDueBetweenAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
        => await db.Set<WorkItem>()
            .Where(x => !x.IsDeleted && !x.IsCompleted && x.DueDate != null && x.DueDate >= fromUtc && x.DueDate < toUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<(Guid TaskId, Guid StatusId, DateTimeOffset EnteredAtUtc)>> ListOpenTaskStatusAgesAsync(CancellationToken ct = default)
    {
        var open = await db.Set<WorkItem>()
            .Where(x => !x.IsDeleted && !x.IsCompleted)
            .Select(x => new { x.Id, x.StatusId, x.CreatedAtUtc })
            .ToListAsync(ct);

        if (open.Count == 0)
        {
            return Array.Empty<(Guid, Guid, DateTimeOffset)>();
        }

        var taskIds = open.Select(o => o.Id).ToList();
        var lastStatusChange = await db.Set<TaskActivityEvent>()
            .Where(a => taskIds.Contains(a.TaskId) && a.Type == "status_changed")
            .GroupBy(a => a.TaskId)
            .Select(g => new { TaskId = g.Key, LastAtUtc = g.Max(a => a.CreatedAtUtc) })
            .ToDictionaryAsync(x => x.TaskId, x => x.LastAtUtc, ct);

        return open
            .Select(o => (o.Id, o.StatusId, lastStatusChange.TryGetValue(o.Id, out var lastAtUtc) ? lastAtUtc : o.CreatedAtUtc))
            .ToList();
    }
}

internal sealed class TaskListMembershipStore(PlanvexaDbContext db) : ITaskListMembershipStore
{
    public void Add(TaskListMembership membership) => db.Set<TaskListMembership>().Add(membership);

    public void Remove(TaskListMembership membership) => db.Set<TaskListMembership>().Remove(membership);

    public Task<TaskListMembership?> FindAsync(Guid taskId, Guid listId, CancellationToken ct = default)
        => db.Set<TaskListMembership>().FirstOrDefaultAsync(x => x.TaskId == taskId && x.ListId == listId, ct);

    public Task<TaskListMembership?> FindPrimaryAsync(Guid taskId, CancellationToken ct = default)
        => db.Set<TaskListMembership>().FirstOrDefaultAsync(x => x.TaskId == taskId && x.IsPrimary, ct);

    public async Task<IReadOnlyList<TaskListMembership>> ListForTaskAsync(Guid taskId, CancellationToken ct = default)
        => await db.Set<TaskListMembership>().Where(x => x.TaskId == taskId).ToListAsync(ct);

    public Task<int> CountForListAsync(Guid listId, CancellationToken ct = default)
        => db.Set<TaskListMembership>().CountAsync(x => x.ListId == listId, ct);
}

internal sealed class TaskTypeStore(PlanvexaDbContext db) : ITaskTypeStore
{
    public void Add(TaskType type) => db.Set<TaskType>().Add(type);

    public Task<TaskType?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<TaskType>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<TaskType>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<TaskType>().Where(x => x.WorkspaceId == workspaceId).OrderBy(x => x.Position).ToListAsync(ct);

    public Task<TaskType?> FindBuiltInAsync(Guid workspaceId, CancellationToken ct = default)
        => db.Set<TaskType>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.IsBuiltIn, ct);
}

internal sealed class TaskRelationStore(PlanvexaDbContext db) : ITaskRelationStore
{
    public void Add(TaskRelation relation) => db.Set<TaskRelation>().Add(relation);

    public void Remove(TaskRelation relation) => db.Set<TaskRelation>().Remove(relation);

    public Task<TaskRelation?> FindAsync(Guid taskId, Guid relatedTaskId, CancellationToken ct = default)
        => db.Set<TaskRelation>().FirstOrDefaultAsync(x =>
            (x.TaskId == taskId && x.RelatedTaskId == relatedTaskId) ||
            (x.TaskId == relatedTaskId && x.RelatedTaskId == taskId), ct);

    public async Task<IReadOnlyList<TaskRelation>> ListForTaskAsync(Guid taskId, CancellationToken ct = default)
        => await db.Set<TaskRelation>().Where(x => x.TaskId == taskId || x.RelatedTaskId == taskId).ToListAsync(ct);
}

internal sealed class DependencyStore(PlanvexaDbContext db) : IDependencyStore
{
    public void Add(TaskDependency dependency) => db.Set<TaskDependency>().Add(dependency);

    public void Remove(TaskDependency dependency) => db.Set<TaskDependency>().Remove(dependency);

    public Task<TaskDependency?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<TaskDependency>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<TaskDependency>> ListForTaskAsync(Guid taskId, CancellationToken ct = default)
        => await db.Set<TaskDependency>().Where(x => x.TaskId == taskId).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> IncompleteBlockersAsync(Guid taskId, CancellationToken ct = default)
    {
        // Tasks that block `taskId`: those it is BlockedBy, plus those that explicitly Block it.
        var blockedByIds = db.Set<TaskDependency>()
            .Where(d => d.TaskId == taskId && d.Type == DependencyType.BlockedBy)
            .Select(d => d.DependsOnTaskId);

        var blocksIds = db.Set<TaskDependency>()
            .Where(d => d.DependsOnTaskId == taskId && d.Type == DependencyType.Blocks)
            .Select(d => d.TaskId);

        var blockerIds = blockedByIds.Union(blocksIds);

        return await db.Set<WorkItem>()
            .Where(t => blockerIds.Contains(t.Id) && !t.IsCompleted && !t.IsDeleted)
            .Select(t => t.Id)
            .ToListAsync(ct);
    }
}

internal sealed class ChecklistStore(PlanvexaDbContext db) : IChecklistStore
{
    public void Add(TaskChecklist checklist) => db.Set<TaskChecklist>().Add(checklist);

    public Task<TaskChecklist?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<TaskChecklist>().Include(c => c.Items).FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<TaskChecklistItem?> FindItemAsync(Guid itemId, CancellationToken ct = default)
        => db.Set<TaskChecklistItem>().FirstOrDefaultAsync(x => x.Id == itemId, ct);

    public async Task<double?> MaxItemPositionAsync(Guid checklistId, CancellationToken ct = default)
        => await db.Set<TaskChecklistItem>().Where(x => x.ChecklistId == checklistId)
            .Select(x => (double?)x.Position).MaxAsync(ct);

    public async Task<IReadOnlyList<TaskChecklist>> ListForTaskAsync(Guid taskId, CancellationToken ct = default)
        => await db.Set<TaskChecklist>().Include(c => c.Items)
            .Where(x => x.TaskId == taskId).OrderBy(x => x.Position).ToListAsync(ct);
}

internal sealed class CustomFieldStore(PlanvexaDbContext db) : ICustomFieldStore
{
    public void Add(CustomFieldDefinition definition) => db.Set<CustomFieldDefinition>().Add(definition);

    public void AddValue(CustomFieldValue value) => db.Set<CustomFieldValue>().Add(value);

    public Task<CustomFieldDefinition?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<CustomFieldDefinition>().Include(d => d.Options).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<CustomFieldDefinition>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<CustomFieldDefinition>().Include(d => d.Options)
            .Where(x => x.WorkspaceId == workspaceId).OrderBy(x => x.Position).ToListAsync(ct);

    public Task<CustomFieldValue?> FindValueAsync(Guid taskId, Guid definitionId, CancellationToken ct = default)
        => db.Set<CustomFieldValue>().FirstOrDefaultAsync(x => x.TaskId == taskId && x.DefinitionId == definitionId, ct);

    public async Task<IReadOnlyList<CustomFieldValue>> ListValuesForTaskAsync(Guid taskId, CancellationToken ct = default)
        => await db.Set<CustomFieldValue>().Where(x => x.TaskId == taskId).ToListAsync(ct);

    public void AddRelationshipValue(CustomFieldRelationshipValue value) => db.Set<CustomFieldRelationshipValue>().Add(value);

    public void RemoveRelationshipValue(CustomFieldRelationshipValue value) => db.Set<CustomFieldRelationshipValue>().Remove(value);

    public async Task<IReadOnlyList<CustomFieldRelationshipValue>> ListRelationshipValuesAsync(Guid taskId, Guid definitionId, CancellationToken ct = default)
        => await db.Set<CustomFieldRelationshipValue>()
            .Where(x => x.TaskId == taskId && x.DefinitionId == definitionId).ToListAsync(ct);
}

internal sealed class RecurringTaskStore(PlanvexaDbContext db) : IRecurringTaskStore
{
    public void Add(RecurringTaskDefinition definition) => db.Set<RecurringTaskDefinition>().Add(definition);

    public void AddOccurrence(RecurringOccurrence occurrence) => db.Set<RecurringOccurrence>().Add(occurrence);

    public Task<RecurringTaskDefinition?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<RecurringTaskDefinition>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> OccurrenceExistsAsync(Guid definitionId, string occurrenceKey, CancellationToken ct = default)
        => db.Set<RecurringOccurrence>().AnyAsync(x => x.DefinitionId == definitionId && x.OccurrenceKey == occurrenceKey, ct);

    public async Task<IReadOnlyList<RecurringTaskDefinition>> ListDueAsync(DateTimeOffset nowUtc, int max, CancellationToken ct = default)
        => await db.Set<RecurringTaskDefinition>().IgnoreQueryFilters()
            .Where(x => x.IsActive && x.NextRunUtc <= nowUtc)
            .OrderBy(x => x.NextRunUtc).Take(max).ToListAsync(ct);
}

internal sealed class SavedViewStore(PlanvexaDbContext db) : ISavedViewStore
{
    public void Add(SavedView view) => db.Set<SavedView>().Add(view);

    public Task<SavedView?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<SavedView>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SavedView>> ListForUserAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
        => await db.Set<SavedView>()
            .Where(x => x.WorkspaceId == workspaceId && (!x.IsPrivate || x.OwnerUserId == userId))
            .OrderBy(x => x.Name).ToListAsync(ct);
}

internal sealed class AttachmentStore(PlanvexaDbContext db) : IAttachmentStore
{
    public void Add(TaskAttachment attachment) => db.Set<TaskAttachment>().Add(attachment);

    public void Remove(TaskAttachment attachment) => db.Set<TaskAttachment>().Remove(attachment);

    public Task<TaskAttachment?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<TaskAttachment>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<TaskAttachment>> ListForTaskAsync(Guid taskId, CancellationToken ct = default)
        => await db.Set<TaskAttachment>()
            .Where(x => x.TaskId == taskId)
            .OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);
}

internal sealed class ReminderStore(PlanvexaDbContext db) : IReminderStore
{
    public void Add(TaskReminder reminder) => db.Set<TaskReminder>().Add(reminder);

    public void Remove(TaskReminder reminder) => db.Set<TaskReminder>().Remove(reminder);

    public Task<TaskReminder?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<TaskReminder>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<TaskReminder>> ListForTaskAsync(Guid taskId, Guid userId, CancellationToken ct = default)
        => await db.Set<TaskReminder>()
            .Where(x => x.TaskId == taskId && x.UserId == userId)
            .OrderBy(x => x.RemindAtUtc).ToListAsync(ct);

    public async Task<IReadOnlyList<TaskReminder>> ListDueAsync(DateTimeOffset nowUtc, int max, CancellationToken ct = default)
        => await db.Set<TaskReminder>().IgnoreQueryFilters()
            .Where(x => !x.IsSent && x.RemindAtUtc <= nowUtc)
            .OrderBy(x => x.RemindAtUtc).Take(max).ToListAsync(ct);
}

/// <summary>
/// Raw name/title-match candidates for "search or jump to", one workspace at a time. Returns full
/// entities (not projections) so SearchService can permission-filter each candidate via
/// WorkManagementAuthorizer before ever building a result out of it — this store does not know about
/// privacy/ACL, same separation as every other *Store in this module (see IActivityStore's doc comment
/// for the same pattern).
/// </summary>
internal sealed class SearchStore(PlanvexaDbContext db) : ISearchStore
{
    // ponytail: unindexed ILIKE '%term%' scan, bounded by one workspace's rows. Ceiling is a few
    // hundred thousand tasks; past that add a tsvector column + GIN index (or an external search
    // service) and swap this implementation — the ISearchStore contract does not change.
    public async Task<IReadOnlyList<Space>> SearchSpacesAsync(Guid workspaceId, string contains, int take, CancellationToken ct = default)
        => await db.Set<Space>()
            .Where(s => s.WorkspaceId == workspaceId && !s.IsDeleted && EF.Functions.ILike(s.Name, contains))
            .OrderBy(s => s.Name).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<Folder>> SearchFoldersAsync(Guid workspaceId, string contains, int take, CancellationToken ct = default)
        => await db.Set<Folder>()
            .Where(f => f.WorkspaceId == workspaceId && !f.IsDeleted && EF.Functions.ILike(f.Name, contains))
            .OrderBy(f => f.Name).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<TaskList>> SearchListsAsync(Guid workspaceId, string contains, int take, CancellationToken ct = default)
        => await db.Set<TaskList>()
            .Where(l => l.WorkspaceId == workspaceId && !l.IsDeleted && EF.Functions.ILike(l.Name, contains))
            .OrderBy(l => l.Name).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<WorkItem>> SearchTasksAsync(
        Guid workspaceId, string contains, string startsWith, int take, CancellationToken ct = default)
        => await db.Set<WorkItem>()
            .Where(t => t.WorkspaceId == workspaceId && !t.IsDeleted && EF.Functions.ILike(t.Title, contains))
            .OrderByDescending(t => EF.Functions.ILike(t.Title, startsWith))
            .ThenByDescending(t => t.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);
}

internal sealed class ActivityStore(PlanvexaDbContext db) : IActivityStore
{
    public void Add(TaskActivityEvent activity) => db.Set<TaskActivityEvent>().Add(activity);

    public async Task<IReadOnlyList<TaskActivityEvent>> ListForTaskAsync(Guid taskId, int max, CancellationToken ct = default)
        => await db.Set<TaskActivityEvent>()
            .Where(x => x.TaskId == taskId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(max).ToListAsync(ct);

    public async Task<IReadOnlyList<TaskActivityEvent>> ListByWorkspaceAsync(
        Guid workspaceId, DateTimeOffset beforeUtc, int take, Guid? actorUserId,
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct = default)
    {
        var query = db.Set<TaskActivityEvent>()
            .Where(x => x.WorkspaceId == workspaceId && x.CreatedAtUtc < beforeUtc);

        if (actorUserId is { } actor)
        {
            query = query.Where(x => x.ActorUserId == actor);
        }

        if (fromUtc is { } from)
        {
            query = query.Where(x => x.CreatedAtUtc >= from);
        }

        if (toUtc is { } to)
        {
            query = query.Where(x => x.CreatedAtUtc <= to);
        }

        return await query.OrderByDescending(x => x.CreatedAtUtc).Take(take).ToListAsync(ct);
    }
}
