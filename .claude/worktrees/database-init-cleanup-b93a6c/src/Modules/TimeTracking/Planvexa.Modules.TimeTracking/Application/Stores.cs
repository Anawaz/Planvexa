namespace Planvexa.Modules.TimeTracking.Application;

using Planvexa.Modules.TimeTracking.Domain;

public interface ITimeEntryStore
{
    void Add(TimeEntry entry);
    void Remove(TimeEntry entry);
    void AddAudit(TimeEntryAudit audit);
    Task<TimeEntry?> FindAsync(Guid id, CancellationToken ct = default);
    Task<TimeEntry?> FindActiveForUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<TimeEntry>> QueryAsync(Guid workspaceId, Guid? userId, Guid? taskId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, Guid? tagId = null, CancellationToken ct = default);
    Task<IReadOnlyList<TimeEntry>> ListForPeriodAsync(Guid workspaceId, Guid userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>Total tracked seconds (all entries, running or not) for a user in [fromUtc, toUtc) -- the missing-time reminder's eligibility input.</summary>
    Task<long> SumDurationSecondsAsync(Guid workspaceId, Guid userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>Offline-mutation-outbox replay guard: the timer entry previously started with this
    /// Idempotency-Key in this workspace, if any (see TimeEntry.IdempotencyKey's doc comment).</summary>
    Task<TimeEntry?> FindByIdempotencyKeyAsync(Guid workspaceId, string key, CancellationToken ct = default);
}

public interface ITimePolicyStore
{
    void Add(TimePolicy policy);
    Task<TimePolicy?> FindAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>Cross-workspace read for the missing-time reminder scheduler (mirrors IDigestPreferenceStore.ListEnabledAsync).</summary>
    Task<IReadOnlyList<TimePolicy>> ListWithReminderEnabledAsync(CancellationToken ct = default);
}

public interface ITimeTagStore
{
    void Add(TimeTag tag);
    Task<TimeTag?> FindByNameAsync(Guid workspaceId, string name, CancellationToken ct = default);
    Task<IReadOnlyList<TimeTag>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>Filters to the subset of tag ids that actually exist in this workspace (mirrors ITagStore.ExistingTagIdsAsync).</summary>
    Task<IReadOnlyList<Guid>> ExistingTagIdsAsync(Guid workspaceId, IReadOnlyCollection<Guid> tagIds, CancellationToken ct = default);
}

public interface IBudgetStore
{
    void Add(Budget budget);
    void Remove(Budget budget);
    Task<Budget?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default);
    Task<Budget?> FindByScopeAsync(Guid workspaceId, BudgetScopeType scopeType, Guid scopeId, CancellationToken ct = default);
    Task<IReadOnlyList<Budget>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface IMemberRateStore
{
    void Add(MemberRate rate);
    Task<MemberRate?> FindAsync(Guid workspaceId, Guid userId, Guid? projectId, CancellationToken ct = default);
    Task<IReadOnlyList<MemberRate>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface ITimesheetStore
{
    void Add(TimesheetPeriod period);
    Task<TimesheetPeriod?> FindAsync(Guid id, CancellationToken ct = default);
    Task<TimesheetPeriod?> FindForUserWeekAsync(Guid workspaceId, Guid userId, DateTimeOffset periodStartUtc, CancellationToken ct = default);
}
