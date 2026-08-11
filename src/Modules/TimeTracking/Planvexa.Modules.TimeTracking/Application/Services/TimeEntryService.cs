namespace Planvexa.Modules.TimeTracking.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.TimeTracking.Authorization;
using Planvexa.Modules.TimeTracking.Domain;
using Planvexa.SharedContracts.Work;

/// <summary>
/// Server-authoritative timers and time entries (ADR-0010). Timer start/stop record server UTC
/// instants; duration is derived from them. The single-active-timer rule is enforced both in the
/// service (fast path) and by a PostgreSQL partial unique index (authoritative under concurrency).
///
/// Documented gap: logged time never appears in a task's WorkManagement Activity feed (spec section 18's
/// "Time logged" event type). TimeEntryAudit above records it within this module, but there is no
/// approved cross-module contract yet for writing into WorkManagement's IActivityStore (AGENTS.md rule
/// 7) — add one (mirroring ITaskDirectory's shape) if/when that surfacing becomes a real requirement.
/// </summary>
public sealed class TimeEntryService(
    TimeServiceContext ctx,
    ITimeEntryStore entries,
    ITaskDirectory tasks,
    ITimeTagStore tags) : TimeServiceBase(ctx)
{
    public async Task<TimeEntryDto> StartTimerAsync(StartTimerCommand command, string? idempotencyKey = null, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureTrackOwn((await AccessAsync(workspaceId, ct))?.Role);

        // Offline-mutation-outbox replay guard: a repeated start with the same Idempotency-Key returns the
        // original timer entry instead of inserting a duplicate (see TimeEntry.IdempotencyKey's doc comment).
        var key = idempotencyKey?.Trim();
        if (!string.IsNullOrEmpty(key))
        {
            var existing = await entries.FindByIdempotencyKeyAsync(workspaceId, key, ct);
            if (existing is not null)
            {
                return await ToDtoAsync(workspaceId, existing, ct);
            }
        }

        var policy = await GetOrCreatePolicyAsync(workspaceId, ct);

        var (taskId, projectId) = await ResolveTaskAsync(command.TaskId, ct);
        if (policy.RequireTask && taskId is null)
        {
            throw new ValidationAppException("This workspace requires a task for time entries.");
        }

        if (policy.SingleActiveTimer)
        {
            var active = await entries.FindActiveForUserAsync(UserId, ct);
            if (active is not null)
            {
                throw new ConflictException("You already have a running timer. Stop it before starting another.");
            }
        }

        var (billing, cost) = await ResolveRatesAsync(workspaceId, UserId, projectId, ct);
        var isBillable = command.IsBillable ?? policy.BillableByDefault;

        var entry = TimeEntry.StartTimer(
            NewId(), workspaceId, UserId, taskId, Now, "UTC", isBillable, billing, cost, command.Description, key);

        entries.Add(entry);
        if (command.TagIds is { Count: > 0 })
        {
            var validTagIds = await tags.ExistingTagIdsAsync(workspaceId, command.TagIds, ct);
            entry.SetTags(validTagIds, NewId, Now);
        }

        entries.AddAudit(new TimeEntryAudit(NewId(), entry.Id, UserId, "timer.started", null, null, Now));
        Audit("time.timer_started", "TimeEntry", entry.Id, new { taskId });
        await SaveAsync(ct);
        await NotifyRealtimeAsync(workspaceId, entry.Id, "started", ct);
        return await ToDtoAsync(workspaceId, entry, ct);
    }

    public async Task<TimeEntryDto?> GetActiveTimerAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureTrackOwn((await AccessAsync(workspaceId, ct))?.Role);
        var active = await entries.FindActiveForUserAsync(UserId, ct);
        return active is null ? null : await ToDtoAsync(workspaceId, active, ct);
    }

    public async Task<TimeEntryDto> StopTimerAsync(StopTimerCommand command, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureTrackOwn((await AccessAsync(workspaceId, ct))?.Role);

        var active = await entries.FindActiveForUserAsync(UserId, ct)
            ?? throw new NotFoundException("You have no running timer.");

        active.Stop(Now, command.Description);
        entries.AddAudit(new TimeEntryAudit(NewId(), active.Id, UserId, "timer.stopped", $"{active.DurationSeconds}s", null, Now));
        Audit("time.timer_stopped", "TimeEntry", active.Id, new { active.DurationSeconds });
        await SaveAsync(ct);
        await NotifyRealtimeAsync(workspaceId, active.Id, "stopped", ct);
        return await ToDtoAsync(workspaceId, active, ct);
    }

    public async Task<TimeEntryDto> PauseTimerAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureTrackOwn((await AccessAsync(workspaceId, ct))?.Role);

        // Scoped to the caller's own active timer (mirrors StopTimerAsync) -- another user's running
        // timer is simply not "theirs", so this 404s rather than ever touching it.
        var active = await entries.FindActiveForUserAsync(UserId, ct)
            ?? throw new NotFoundException("You have no running timer.");

        active.Pause(Now);
        entries.AddAudit(new TimeEntryAudit(NewId(), active.Id, UserId, "timer.paused", null, null, Now));
        Audit("time.timer_paused", "TimeEntry", active.Id);
        await SaveAsync(ct);
        await NotifyRealtimeAsync(workspaceId, active.Id, "paused", ct);
        return await ToDtoAsync(workspaceId, active, ct);
    }

    public async Task<TimeEntryDto> ResumeTimerAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureTrackOwn((await AccessAsync(workspaceId, ct))?.Role);

        var active = await entries.FindActiveForUserAsync(UserId, ct)
            ?? throw new NotFoundException("You have no running timer.");

        active.Resume(Now);
        entries.AddAudit(new TimeEntryAudit(NewId(), active.Id, UserId, "timer.resumed", null, null, Now));
        Audit("time.timer_resumed", "TimeEntry", active.Id);
        await SaveAsync(ct);
        await NotifyRealtimeAsync(workspaceId, active.Id, "resumed", ct);
        return await ToDtoAsync(workspaceId, active, ct);
    }

    public async Task<TimeEntryDto> CreateManualAsync(CreateManualEntryCommand command, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureTrackOwn((await AccessAsync(workspaceId, ct))?.Role);

        var policy = await GetOrCreatePolicyAsync(workspaceId, ct);
        var (taskId, projectId) = await ResolveTaskAsync(command.TaskId, ct);

        if (policy.RequireTask && taskId is null)
        {
            throw new ValidationAppException("This workspace requires a task for time entries.");
        }

        if (policy.RequireDescription && string.IsNullOrWhiteSpace(command.Description))
        {
            throw new ValidationAppException("This workspace requires a description for time entries.");
        }

        var endedAt = command.EndedAtUtc
            ?? (command.DurationSeconds is { } secs ? command.StartedAtUtc.AddSeconds(secs) : throw new ValidationAppException("Provide an end time or a duration."));

        var durationSeconds = TimeMath.DurationSeconds(command.StartedAtUtc, endedAt);
        if (durationSeconds > policy.MaximumEntrySeconds)
        {
            throw new ValidationAppException($"A single entry cannot exceed {policy.MaximumEntrySeconds / 3600d:0.#} hours.");
        }

        var (billing, cost) = await ResolveRatesAsync(workspaceId, UserId, projectId, ct);
        var isBillable = command.IsBillable ?? policy.BillableByDefault;
        var timeZoneId = string.IsNullOrWhiteSpace(command.TimeZoneId) ? "UTC" : command.TimeZoneId!;

        var entry = TimeEntry.CreateManual(
            NewId(), workspaceId, UserId, taskId, command.StartedAtUtc, endedAt, timeZoneId, isBillable, billing, cost, command.Description, Now);

        entries.Add(entry);
        if (command.TagIds is { Count: > 0 })
        {
            var validTagIds = await tags.ExistingTagIdsAsync(workspaceId, command.TagIds, ct);
            entry.SetTags(validTagIds, NewId, Now);
        }

        entries.AddAudit(new TimeEntryAudit(NewId(), entry.Id, UserId, "entry.created", $"{durationSeconds}s", null, Now));
        Audit("time.entry_created", "TimeEntry", entry.Id, new { taskId, durationSeconds });
        await SaveAsync(ct);
        return await ToDtoAsync(workspaceId, entry, ct);
    }

    public async Task<IReadOnlyList<TimeEntryDto>> QueryAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, Guid? userId, Guid? taskId, Guid? tagId, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        var access = await AccessAsync(workspaceId, ct);
        TimeAuthorizer.EnsureTrackOwn(access?.Role);

        // Members only see their own entries unless they can manage.
        var effectiveUserId = TimeAuthorizer.CanManage(access?.Role) ? userId : UserId;
        var list = await entries.QueryAsync(workspaceId, effectiveUserId ?? UserId, taskId, fromUtc, toUtc, tagId, ct);
        return await ToDtoListAsync(workspaceId, list, ct);
    }

    public async Task<TimeEntryDto> UpdateAsync(Guid entryId, UpdateEntryCommand command, CancellationToken ct = default)
    {
        var (entry, workspaceId) = await LoadForEditAsync(entryId, ct);
        entry.AdjustTimes(command.StartedAtUtc, command.EndedAtUtc, command.Description, command.IsBillable, command.Reason, Now);
        if (command.TagIds is not null)
        {
            var validTagIds = await tags.ExistingTagIdsAsync(workspaceId, command.TagIds, ct);
            entry.SetTags(validTagIds, NewId, Now);
        }

        entries.AddAudit(new TimeEntryAudit(NewId(), entry.Id, UserId, "entry.edited", $"{entry.DurationSeconds}s", command.Reason, Now));
        Audit("time.entry_edited", "TimeEntry", entry.Id, new { entry.DurationSeconds, reason = command.Reason });
        await SaveAsync(ct);
        return await ToDtoAsync(workspaceId, entry, ct);
    }

    public async Task<TimeEntryDto> MoveAsync(Guid entryId, Guid? taskId, string? reason, CancellationToken ct = default)
    {
        var (entry, workspaceId) = await LoadForEditAsync(entryId, ct);
        var (resolvedTaskId, projectId) = await ResolveTaskAsync(taskId, ct);
        entry.MoveToTask(resolvedTaskId, reason, Now);

        var (billing, cost) = await ResolveRatesAsync(workspaceId, entry.UserId, projectId, ct);
        entry.ApplyResolvedRates(billing, cost);

        entries.AddAudit(new TimeEntryAudit(NewId(), entry.Id, UserId, "entry.moved", resolvedTaskId?.ToString(), reason, Now));
        Audit("time.entry_moved", "TimeEntry", entry.Id, new { taskId = resolvedTaskId });
        await SaveAsync(ct);
        return await ToDtoAsync(workspaceId, entry, ct);
    }

    public async Task<IReadOnlyList<TimeEntryDto>> SplitAsync(Guid entryId, DateTimeOffset atUtc, string? reason, CancellationToken ct = default)
    {
        var (entry, workspaceId) = await LoadForEditAsync(entryId, ct);
        var remainder = entry.SplitAt(NewId(), atUtc, reason, Now);
        entries.Add(remainder);
        entries.AddAudit(new TimeEntryAudit(NewId(), entry.Id, UserId, "entry.split", atUtc.ToString("O"), reason, Now));
        Audit("time.entry_split", "TimeEntry", entry.Id, new { atUtc });
        await SaveAsync(ct);
        return await ToDtoListAsync(workspaceId, new[] { entry, remainder }, ct);
    }

    public async Task DeleteAsync(Guid entryId, string? reason, CancellationToken ct = default)
    {
        var (entry, _) = await LoadForEditAsync(entryId, ct);
        if (entry.IsImmutable)
        {
            throw new ConflictException("Approved or locked time entries cannot be deleted.");
        }

        // Soft delete is not modelled for time entries; mark via audit + physical removal is avoided.
        // Instead we reject deletion of immutable entries above and stamp an audit for the deletion.
        entries.AddAudit(new TimeEntryAudit(NewId(), entry.Id, UserId, "entry.deleted", null, reason, Now));
        Audit("time.entry_deleted", "TimeEntry", entry.Id);
        entries.Remove(entry);
        await SaveAsync(ct);
    }

    private async Task<(TimeEntry Entry, Guid WorkspaceId)> LoadForEditAsync(Guid entryId, CancellationToken ct)
    {
        var entry = await entries.FindAsync(entryId, ct) ?? throw new NotFoundException("Time entry not found.");
        var access = await AccessAsync(entry.WorkspaceId, ct);
        TimeAuthorizer.EnsureCanActOnEntry(access?.Role, entry.UserId, UserId);
        return (entry, entry.WorkspaceId);
    }

    private async Task<(Guid? TaskId, Guid? ProjectId)> ResolveTaskAsync(Guid? taskId, CancellationToken ct)
    {
        if (taskId is null)
        {
            return (null, null);
        }

        var task = await tasks.FindAsync(taskId.Value, ct)
            ?? throw new NotFoundException("Task not found.");
        return (task.TaskId, task.ListId);
    }

    private async Task<TimeEntryDto> ToDtoAsync(Guid workspaceId, TimeEntry entry, CancellationToken ct)
    {
        var byEntry = await ResolveTagRefsAsync(workspaceId, new[] { entry }, ct);
        return TimeMapper.ToDto(entry, byEntry[entry.Id]);
    }

    private async Task<IReadOnlyList<TimeEntryDto>> ToDtoListAsync(Guid workspaceId, IReadOnlyCollection<TimeEntry> list, CancellationToken ct)
    {
        var byEntry = await ResolveTagRefsAsync(workspaceId, list, ct);
        return list.Select(e => TimeMapper.ToDto(e, byEntry[e.Id])).ToList();
    }

    /// <summary>
    /// Resolves each entry's tag ids to (id, name) pairs in one workspace-tag lookup rather than one
    /// per entry. ponytail: loads the whole workspace tag list; fine at the small counts a tag list
    /// realistically reaches, revisit with a targeted "tags by id" lookup if that stops being true.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<TimeTagRef>>> ResolveTagRefsAsync(
        Guid workspaceId, IReadOnlyCollection<TimeEntry> list, CancellationToken ct)
    {
        if (list.All(e => e.Tags.Count == 0))
        {
            return list.ToDictionary(e => e.Id, _ => (IReadOnlyList<TimeTagRef>)Array.Empty<TimeTagRef>());
        }

        var byId = (await tags.ListByWorkspaceAsync(workspaceId, ct)).ToDictionary(t => t.Id);
        return list.ToDictionary(
            e => e.Id,
            e => (IReadOnlyList<TimeTagRef>)e.Tags
                .Select(t => byId.TryGetValue(t.TagId, out var tag) ? new TimeTagRef(tag.Id, tag.Name) : new TimeTagRef(t.TagId, "Deleted tag"))
                .ToList());
    }
}
