namespace Planvexa.Modules.TimeTracking.Domain;

/// <summary>How a time entry was recorded.</summary>
public enum TimeEntrySource
{
    Timer = 0,
    Manual = 1,
}

/// <summary>Approval lifecycle of a time entry / timesheet.</summary>
public enum ApprovalStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Locked = 4,
}

public enum TimesheetCadence
{
    Weekly = 0,
    Daily = 1,
}

/// <summary>How often a missing-time reminder is evaluated for a workspace.</summary>
public enum MissingTimeReminderCadence
{
    Daily = 0,
    Weekly = 1,
}

/// <summary>What a <see cref="Budget"/> is scoped to: a Space or a List (a "project" -- see MemberRate.ProjectId).</summary>
public enum BudgetScopeType
{
    Space = 0,
    List = 1,
}
