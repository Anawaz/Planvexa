namespace Planvexa.Modules.TimeTracking.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>Per-workspace time-tracking policy. One row per workspace (created with defaults on demand).</summary>
public sealed class TimePolicy : Entity, IWorkspaceOwned
{
    private TimePolicy()
    {
    }

    private TimePolicy(Guid id, Guid workspaceId)
        : base(id)
    {
        WorkspaceId = workspaceId;
        SingleActiveTimer = true;
        RoundingMinutes = 0;
        MinimumDurationSeconds = 0;
        MaximumEntrySeconds = 24 * 3600;
        BillableByDefault = true;
        RequireDescription = false;
        RequireTask = false;
        EditWindowHours = 0;
        ApprovalRequired = false;
        WeekStartsOn = 1; // Monday
        OvertimeThresholdSeconds = 40 * 3600;
        MissingTimeReminderEnabled = false;
        MissingTimeReminderCadence = MissingTimeReminderCadence.Daily;
        MissingTimeReminderMinimumSeconds = 0;
    }

    public Guid WorkspaceId { get; private set; }
    public bool SingleActiveTimer { get; private set; }
    public int RoundingMinutes { get; private set; }
    public long MinimumDurationSeconds { get; private set; }
    public long MaximumEntrySeconds { get; private set; }
    public bool BillableByDefault { get; private set; }
    public bool RequireDescription { get; private set; }
    public bool RequireTask { get; private set; }
    public int EditWindowHours { get; private set; }
    public bool ApprovalRequired { get; private set; }

    /// <summary>0=Sunday … 6=Saturday.</summary>
    public int WeekStartsOn { get; private set; }

    /// <summary>Entries dated on/before this instant are locked from editing. Null means no lock.</summary>
    public DateTimeOffset? LockDateUtc { get; private set; }

    public long OvertimeThresholdSeconds { get; private set; }

    /// <summary>When true, members with insufficient tracked time for the period get a reminder notification.</summary>
    public bool MissingTimeReminderEnabled { get; private set; }

    /// <summary>Whether the "insufficient time" check runs once per day or once per week.</summary>
    public MissingTimeReminderCadence MissingTimeReminderCadence { get; private set; }

    /// <summary>Tracked seconds below this in the period makes a member eligible for a reminder.</summary>
    public long MissingTimeReminderMinimumSeconds { get; private set; }

    public static TimePolicy CreateDefault(Guid id, Guid workspaceId)
        => new(id, workspaceId);

    public void Update(
        bool singleActiveTimer, int roundingMinutes, long minimumDurationSeconds, long maximumEntrySeconds,
        bool billableByDefault, bool requireDescription, bool requireTask, int editWindowHours,
        bool approvalRequired, int weekStartsOn, DateTimeOffset? lockDateUtc, long overtimeThresholdSeconds,
        bool missingTimeReminderEnabled = false, MissingTimeReminderCadence missingTimeReminderCadence = MissingTimeReminderCadence.Daily,
        long missingTimeReminderMinimumSeconds = 0)
    {
        SingleActiveTimer = singleActiveTimer;
        RoundingMinutes = Math.Max(0, roundingMinutes);
        MinimumDurationSeconds = Math.Max(0, minimumDurationSeconds);
        MaximumEntrySeconds = maximumEntrySeconds <= 0 ? 24 * 3600 : maximumEntrySeconds;
        BillableByDefault = billableByDefault;
        RequireDescription = requireDescription;
        RequireTask = requireTask;
        EditWindowHours = Math.Max(0, editWindowHours);
        ApprovalRequired = approvalRequired;
        WeekStartsOn = ((weekStartsOn % 7) + 7) % 7;
        LockDateUtc = lockDateUtc;
        OvertimeThresholdSeconds = Math.Max(0, overtimeThresholdSeconds);
        MissingTimeReminderEnabled = missingTimeReminderEnabled;
        MissingTimeReminderCadence = missingTimeReminderCadence;
        MissingTimeReminderMinimumSeconds = Math.Max(0, missingTimeReminderMinimumSeconds);
    }
}

/// <summary>Per-member billing (revenue) and cost rates, optionally scoped to a project/list.</summary>
public sealed class MemberRate : Entity, IWorkspaceOwned
{
    private MemberRate()
    {
    }

    private MemberRate(Guid id, Guid workspaceId, Guid userId, Guid? projectId, decimal billingRate, decimal costRate)
        : base(id)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        ProjectId = projectId;
        BillingRate = billingRate;
        CostRate = costRate;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>Null = the member's default workspace rate; otherwise a project-specific override.</summary>
    public Guid? ProjectId { get; private set; }

    public decimal BillingRate { get; private set; }
    public decimal CostRate { get; private set; }

    public static MemberRate Create(Guid id, Guid workspaceId, Guid userId, Guid? projectId, decimal billingRate, decimal costRate)
        => new(id, workspaceId, userId, projectId, billingRate, costRate);

    public void Update(decimal billingRate, decimal costRate)
    {
        BillingRate = billingRate;
        CostRate = costRate;
    }
}

/// <summary>Append-only audit trail for edits to a time entry (ADR-0010 auditability).</summary>
public sealed class TimeEntryAudit : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private TimeEntryAudit()
    {
    }

    public TimeEntryAudit(Guid id, Guid timeEntryId, Guid actorUserId, string action, string? detail, string? reason, DateTimeOffset createdAtUtc)
        : base(id)
    {
        TimeEntryId = timeEntryId;
        ActorUserId = actorUserId;
        Action = action;
        Detail = detail;
        Reason = reason;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid TimeEntryId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? Detail { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
