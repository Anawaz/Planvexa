namespace Planvexa.Modules.TimeTracking.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.TimeTracking.Domain.Events;

/// <summary>
/// A unit of tracked time (ADR-0010). Server-authoritative: <see cref="StartedAtUtc"/> and
/// <see cref="EndedAtUtc"/> are server instants and <see cref="DurationSeconds"/> is derived from
/// them. A running timer has a null <see cref="EndedAtUtc"/>. Money uses decimal. Approved/locked
/// entries are immutable except through a controlled correction (a reason is required).
/// </summary>
public sealed class TimeEntry : Entity, IAggregateRoot, IWorkspaceOwned
{
    private readonly List<TimeEntryTag> _tags = new();

    private TimeEntry()
    {
    }

    private TimeEntry(
        Guid id, Guid workspaceId, Guid userId, Guid? taskId,
        DateTimeOffset startedAtUtc, string timeZoneId, TimeEntrySource source, bool isBillable,
        decimal billingRate, decimal costRate, string? description, string? idempotencyKey)
        : base(id)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        TaskId = taskId;
        StartedAtUtc = startedAtUtc;
        TimeZoneId = timeZoneId;
        Source = source;
        IsBillable = isBillable;
        BillingRate = billingRate;
        CostRate = costRate;
        Description = description;
        IdempotencyKey = idempotencyKey;
        ApprovalStatus = ApprovalStatus.Draft;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? TaskId { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }

    /// <summary>Persisted duration in whole seconds, derived from server timestamps. 0 while running.</summary>
    public long DurationSeconds { get; private set; }

    public string TimeZoneId { get; private set; } = "UTC";
    public string? Description { get; private set; }
    public bool IsBillable { get; private set; }
    public decimal BillingRate { get; private set; }
    public decimal CostRate { get; private set; }
    public TimeEntrySource Source { get; private set; }

    /// <summary>Offline-mutation-outbox replay guard: see WorkItem.IdempotencyKey's doc comment for the
    /// pattern (nullable, unique per workspace when set, checked before creating). Only timer starts use
    /// this — stop is a state transition on an existing row, not a create, so it has no double-creation risk.</summary>
    public string? IdempotencyKey { get; private set; }

    public ApprovalStatus ApprovalStatus { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? LockedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public bool IsRunning => EndedAtUtc is null;
    public bool IsImmutable => ApprovalStatus is ApprovalStatus.Approved or ApprovalStatus.Locked;

    public IReadOnlyList<TimeEntryTag> Tags => _tags.AsReadOnly();

    /// <summary>Starts a running timer (no end yet). Duration is computed on stop.</summary>
    public static TimeEntry StartTimer(
        Guid id, Guid workspaceId, Guid userId, Guid? taskId, DateTimeOffset nowUtc,
        string timeZoneId, bool isBillable, decimal billingRate, decimal costRate, string? description,
        string? idempotencyKey = null)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(userId, nameof(userId));

        var entry = new TimeEntry(id, workspaceId, userId, taskId, nowUtc, timeZoneId, TimeEntrySource.Timer, isBillable, billingRate, costRate, description, idempotencyKey)
        {
            CreatedAtUtc = nowUtc,
        };
        return entry;
    }

    /// <summary>Creates a completed manual entry. Duration is computed from the instants.</summary>
    public static TimeEntry CreateManual(
        Guid id, Guid workspaceId, Guid userId, Guid? taskId,
        DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, string timeZoneId, bool isBillable,
        decimal billingRate, decimal costRate, string? description, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(userId, nameof(userId));
        if (endedAtUtc < startedAtUtc)
        {
            throw new ValidationAppException("End time must be on or after start time.");
        }

        var entry = new TimeEntry(id, workspaceId, userId, taskId, startedAtUtc, timeZoneId, TimeEntrySource.Manual, isBillable, billingRate, costRate, description, idempotencyKey: null)
        {
            CreatedAtUtc = nowUtc,
        };
        entry.EndedAtUtc = endedAtUtc;
        entry.DurationSeconds = TimeMath.DurationSeconds(startedAtUtc, endedAtUtc);
        if (taskId is { } loggedTaskId)
        {
            entry.Raise(new TimeEntryLoggedIntegrationEvent(workspaceId, loggedTaskId, id, userId, entry.DurationSeconds));
        }

        return entry;
    }

    /// <summary>Stops a running timer, computing the duration from server timestamps.</summary>
    public void Stop(DateTimeOffset nowUtc, string? description)
    {
        if (!IsRunning)
        {
            throw new ConflictException("This timer is already stopped.");
        }

        EndedAtUtc = nowUtc;
        DurationSeconds = TimeMath.DurationSeconds(StartedAtUtc, nowUtc);
        if (!string.IsNullOrWhiteSpace(description))
        {
            Description = description;
        }

        UpdatedAtUtc = nowUtc;
        if (TaskId is { } loggedTaskId)
        {
            Raise(new TimeEntryLoggedIntegrationEvent(WorkspaceId, loggedTaskId, Id, UserId, DurationSeconds));
        }
    }

    /// <summary>
    /// Adjusts the entry's instants (edit/correction). If the entry is approved/locked, a non-empty
    /// reason is required and the entry returns to Draft for re-approval (controlled correction flow).
    /// </summary>
    public void AdjustTimes(DateTimeOffset? startedAtUtc, DateTimeOffset? endedAtUtc, string? description, bool? isBillable, string? reason, DateTimeOffset nowUtc)
    {
        RequireMutable(reason);

        var newStart = startedAtUtc ?? StartedAtUtc;
        var newEnd = endedAtUtc ?? EndedAtUtc;
        if (newEnd is { } end)
        {
            if (end < newStart)
            {
                throw new ValidationAppException("End time must be on or after start time.");
            }

            DurationSeconds = TimeMath.DurationSeconds(newStart, end);
        }

        StartedAtUtc = newStart;
        EndedAtUtc = newEnd;
        if (description is not null)
        {
            Description = description;
        }

        if (isBillable.HasValue)
        {
            IsBillable = isBillable.Value;
        }

        UpdatedAtUtc = nowUtc;
    }

    public void MoveToTask(Guid? taskId, string? reason, DateTimeOffset nowUtc)
    {
        RequireMutable(reason);
        TaskId = taskId;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Truncates this entry at <paramref name="atUtc"/> and returns the remainder as a new entry.</summary>
    public TimeEntry SplitAt(Guid newId, DateTimeOffset atUtc, string? reason, DateTimeOffset nowUtc)
    {
        RequireMutable(reason);
        if (IsRunning)
        {
            throw new ConflictException("Stop the timer before splitting the entry.");
        }

        if (atUtc <= StartedAtUtc || atUtc >= EndedAtUtc)
        {
            throw new ValidationAppException("The split point must fall strictly within the entry.");
        }

        var originalEnd = EndedAtUtc!.Value;

        // Shorten this entry to [start, atUtc).
        EndedAtUtc = atUtc;
        DurationSeconds = TimeMath.DurationSeconds(StartedAtUtc, atUtc);
        UpdatedAtUtc = nowUtc;

        // Remainder [atUtc, originalEnd).
        return CreateManual(newId, WorkspaceId, UserId, TaskId, atUtc, originalEnd, TimeZoneId, IsBillable, BillingRate, CostRate, Description, nowUtc);
    }

    public void Submit()
    {
        if (IsRunning)
        {
            throw new ConflictException("Stop the timer before submitting.");
        }

        if (ApprovalStatus is ApprovalStatus.Draft or ApprovalStatus.Rejected)
        {
            ApprovalStatus = ApprovalStatus.Submitted;
        }
    }

    public void Approve(Guid approverUserId, DateTimeOffset nowUtc)
    {
        ApprovalStatus = ApprovalStatus.Approved;
        ApprovedByUserId = approverUserId;
        UpdatedAtUtc = nowUtc;
    }

    public void Reject(DateTimeOffset nowUtc)
    {
        ApprovalStatus = ApprovalStatus.Rejected;
        ApprovedByUserId = null;
        UpdatedAtUtc = nowUtc;
    }

    public void Lock(DateTimeOffset nowUtc)
    {
        ApprovalStatus = ApprovalStatus.Locked;
        LockedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void ApplyResolvedRates(decimal billingRate, decimal costRate)
    {
        BillingRate = billingRate;
        CostRate = costRate;
    }

    /// <summary>Replaces the full tag set. <paramref name="tagIdFactory"/> supplies join-row ids (mirrors WorkItem.SetTags).</summary>
    public void SetTags(IReadOnlyCollection<Guid> tagIds, Func<Guid> tagIdFactory, DateTimeOffset nowUtc)
    {
        _tags.RemoveAll(t => !tagIds.Contains(t.TagId));
        foreach (var tagId in tagIds)
        {
            if (_tags.All(t => t.TagId != tagId))
            {
                _tags.Add(new TimeEntryTag(tagIdFactory(), Id, tagId));
            }
        }

        UpdatedAtUtc = nowUtc;
    }

    private void RequireMutable(string? reason)
    {
        if (ApprovalStatus == ApprovalStatus.Locked)
        {
            throw new ConflictException("This time entry is in a locked accounting period and cannot be changed.");
        }

        if (ApprovalStatus == ApprovalStatus.Approved)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ConflictException("An approved time entry can only be changed with a correction reason.");
            }

            // Controlled correction: the entry returns to Draft and must be re-approved.
            ApprovalStatus = ApprovalStatus.Draft;
            ApprovedByUserId = null;
        }
    }
}
