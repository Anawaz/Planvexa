namespace Planvexa.Modules.TimeTracking.Application;

using Planvexa.Modules.TimeTracking.Domain;

// ---- Commands ----
public sealed record StartTimerCommand(Guid? TaskId, string? Description, bool? IsBillable, IReadOnlyCollection<Guid>? TagIds = null);
public sealed record StopTimerCommand(string? Description);
public sealed record CreateManualEntryCommand(Guid? TaskId, DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc, long? DurationSeconds, string? Description, bool? IsBillable, string? TimeZoneId, IReadOnlyCollection<Guid>? TagIds = null);
public sealed record UpdateEntryCommand(DateTimeOffset? StartedAtUtc, DateTimeOffset? EndedAtUtc, string? Description, bool? IsBillable, string? Reason, IReadOnlyCollection<Guid>? TagIds = null);
public sealed record UpdatePolicyCommand(
    bool SingleActiveTimer, int RoundingMinutes, long MinimumDurationSeconds, long MaximumEntrySeconds,
    bool BillableByDefault, bool RequireDescription, bool RequireTask, int EditWindowHours,
    bool ApprovalRequired, int WeekStartsOn, DateTimeOffset? LockDateUtc, long OvertimeThresholdSeconds,
    bool MissingTimeReminderEnabled = false, MissingTimeReminderCadence MissingTimeReminderCadence = MissingTimeReminderCadence.Daily,
    long MissingTimeReminderMinimumSeconds = 0);
public sealed record SetRateCommand(decimal BillingRate, decimal CostRate);
public sealed record CreateTimeTagCommand(string Name);
public sealed record CreateBudgetCommand(BudgetScopeType ScopeType, Guid ScopeId, string Name, decimal? MonetaryCapAmount, long? TimeCapSeconds);
public sealed record UpdateBudgetCommand(string Name, decimal? MonetaryCapAmount, long? TimeCapSeconds);

// ---- Read models ----
public sealed record TimeTagRef(Guid Id, string Name);

public sealed record TimeEntryDto(
    Guid Id, Guid UserId, Guid? TaskId, DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc,
    long DurationSeconds, string TimeZoneId, string? Description, bool IsBillable,
    decimal BillingRate, decimal CostRate, string Source, string ApprovalStatus,
    IReadOnlyList<TimeTagRef> Tags);

public sealed record TimePolicyDto(
    bool SingleActiveTimer, int RoundingMinutes, long MinimumDurationSeconds, long MaximumEntrySeconds,
    bool BillableByDefault, bool RequireDescription, bool RequireTask, int EditWindowHours,
    bool ApprovalRequired, int WeekStartsOn, DateTimeOffset? LockDateUtc, long OvertimeThresholdSeconds,
    bool MissingTimeReminderEnabled, MissingTimeReminderCadence MissingTimeReminderCadence, long MissingTimeReminderMinimumSeconds);

public sealed record MemberRateDto(Guid UserId, decimal BillingRate, decimal CostRate);

public sealed record TimesheetDto(
    Guid Id, Guid UserId, DateTimeOffset PeriodStartUtc, DateTimeOffset PeriodEndUtc, string Status,
    long TotalSeconds, long BillableSeconds, decimal Revenue, decimal Cost, IReadOnlyList<TimeEntryDto> Entries);

public sealed record ReportRowDto(string Key, string Label, decimal Hours, decimal BillableHours, decimal Cost, decimal Revenue);

public sealed record UtilizationRowDto(Guid UserId, decimal TrackedHours, decimal BillableHours, decimal UtilizationPercent);

public sealed record TimeTagDto(Guid Id, string Name);

public sealed record BudgetDto(Guid Id, string Name, BudgetScopeType ScopeType, Guid ScopeId, decimal? MonetaryCapAmount, long? TimeCapSeconds);

public sealed record BudgetStatusDto(
    Guid BudgetId, string Name, BudgetScopeType ScopeType, Guid ScopeId,
    decimal? MonetaryCapAmount, long? TimeCapSeconds,
    decimal Hours, decimal Cost, decimal Revenue, decimal Profit,
    decimal? MonetaryConsumedPercent, decimal? TimeConsumedPercent);

internal static class TimeMapper
{
    public static TimeEntryDto ToDto(TimeEntry e) => new(
        e.Id, e.UserId, e.TaskId, e.StartedAtUtc, e.EndedAtUtc, e.DurationSeconds, e.TimeZoneId,
        e.Description, e.IsBillable, e.BillingRate, e.CostRate, e.Source.ToString(), e.ApprovalStatus.ToString(),
        Array.Empty<TimeTagRef>());

    public static TimeEntryDto ToDto(TimeEntry e, IReadOnlyList<TimeTagRef> tags) => new(
        e.Id, e.UserId, e.TaskId, e.StartedAtUtc, e.EndedAtUtc, e.DurationSeconds, e.TimeZoneId,
        e.Description, e.IsBillable, e.BillingRate, e.CostRate, e.Source.ToString(), e.ApprovalStatus.ToString(),
        tags);

    public static TimePolicyDto ToDto(TimePolicy p) => new(
        p.SingleActiveTimer, p.RoundingMinutes, p.MinimumDurationSeconds, p.MaximumEntrySeconds,
        p.BillableByDefault, p.RequireDescription, p.RequireTask, p.EditWindowHours,
        p.ApprovalRequired, p.WeekStartsOn, p.LockDateUtc, p.OvertimeThresholdSeconds,
        p.MissingTimeReminderEnabled, p.MissingTimeReminderCadence, p.MissingTimeReminderMinimumSeconds);

    public static TimeTagDto ToDto(TimeTag t) => new(t.Id, t.Name);

    public static BudgetDto ToDto(Budget b) => new(b.Id, b.Name, b.ScopeType, b.ScopeId, b.MonetaryCapAmount, b.TimeCapSeconds);

    public static BudgetStatusDto ToDto(BudgetStatus s) => new(
        s.BudgetId, s.Name, s.ScopeType, s.ScopeId, s.MonetaryCapAmount, s.TimeCapSeconds,
        s.Hours, s.Cost, s.Revenue, s.Profit, s.MonetaryConsumedPercent, s.TimeConsumedPercent);
}
