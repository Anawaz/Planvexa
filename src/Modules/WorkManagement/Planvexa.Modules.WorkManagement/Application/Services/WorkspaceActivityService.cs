namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.Modules.WorkManagement.Authorization;

public sealed record ActivityFeedItemDto(Guid Id, Guid TaskId, string TaskTitle, Guid? ActorUserId, string Type, string? Data, DateTimeOffset CreatedAtUtc);

/// <summary>
/// Workspace-wide activity stream (distinct from the existing per-task feed at
/// GET /tasks/{id}/activity, which only ever shows one already-authorized task's own events).
///
/// SECURITY: every event references a Task, and a Member must not see activity for a task they could
/// not otherwise read (private task, or private ancestor list/folder/space, or missing an ACL grant).
/// This deliberately reuses WorkServiceBase.CanReadAsync -- the exact same per-resource ACL/privacy
/// check WorkItemService.ListByListAsync already applies -- rather than trusting a raw workspace-scoped
/// query (e.g. IWorkReportingQueries), which does no ACL/privacy filtering at all and would leak private
/// task titles/activity into this feed.
/// </summary>
public sealed class WorkspaceActivityService(WorkServiceContext ctx, IActivityStore activity, IWorkItemStore tasks) : WorkServiceBase(ctx)
{
    private const int MaxBatches = 5;

    public async Task<IReadOnlyList<ActivityFeedItemDto>> ListAsync(
        DateTimeOffset? beforeUtc, int take, Guid? actorUserId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc,
        CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        take = Math.Clamp(take, 1, 200);
        var cursor = beforeUtc ?? DateTimeOffset.MaxValue;
        var result = new List<ActivityFeedItemDto>(take);

        // ponytail: over-fetch + per-item ACL filter loop (bounded by MaxBatches), same shape as
        // ListByListAsync's per-item CanReadAsync loop -- not a SQL-level ACL join. A workspace where
        // most recent activity belongs to tasks the caller can't read may return fewer than `take` rows
        // per page (never zero unless truly nothing is visible); raise MaxBatches or push privacy into
        // the query if that's ever a real problem.
        for (var batch = 0; batch < MaxBatches && result.Count < take; batch++)
        {
            var events = await activity.ListByWorkspaceAsync(workspaceId, cursor, take, actorUserId, fromUtc, toUtc, ct);
            if (events.Count == 0)
            {
                break;
            }

            cursor = events[^1].CreatedAtUtc;

            var taskIds = events.Select(e => e.TaskId).Distinct().ToList();
            var taskById = (await tasks.ListByIdsAsync(taskIds, ct)).ToDictionary(t => t.Id);

            foreach (var e in events)
            {
                if (result.Count >= take)
                {
                    break;
                }

                if (!taskById.TryGetValue(e.TaskId, out var task))
                {
                    continue; // task hard-deleted; nothing left to ACL-check against
                }

                if (!await CanReadAsync(task, WorkResourceTypes.Task, ct))
                {
                    continue;
                }

                result.Add(new ActivityFeedItemDto(e.Id, e.TaskId, task.Title, e.ActorUserId, e.Type, e.Data, e.CreatedAtUtc));
            }
        }

        return result;
    }
}
