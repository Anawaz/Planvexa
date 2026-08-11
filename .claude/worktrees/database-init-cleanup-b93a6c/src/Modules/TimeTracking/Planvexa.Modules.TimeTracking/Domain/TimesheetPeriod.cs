namespace Planvexa.Modules.TimeTracking.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// A submitted-and-approvable timesheet period for a user (weekly by default). Approving/locking the
/// period is applied to its constituent entries by the service; the period tracks the workflow state.
/// </summary>
public sealed class TimesheetPeriod : Entity, IAggregateRoot, IWorkspaceOwned
{
    private readonly List<TimesheetApproval> _approvals = new();

    private TimesheetPeriod()
    {
    }

    private TimesheetPeriod(Guid id, Guid workspaceId, Guid userId, DateTimeOffset periodStartUtc, DateTimeOffset periodEndUtc, TimesheetCadence cadence)
        : base(id)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        PeriodStartUtc = periodStartUtc;
        PeriodEndUtc = periodEndUtc;
        Cadence = cadence;
        Status = ApprovalStatus.Draft;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset PeriodStartUtc { get; private set; }
    public DateTimeOffset PeriodEndUtc { get; private set; }
    public TimesheetCadence Cadence { get; private set; }
    public ApprovalStatus Status { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }

    public IReadOnlyList<TimesheetApproval> Approvals => _approvals.AsReadOnly();

    public static TimesheetPeriod Create(Guid id, Guid workspaceId, Guid userId, DateTimeOffset periodStartUtc, DateTimeOffset periodEndUtc, TimesheetCadence cadence)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(userId, nameof(userId));
        return new TimesheetPeriod(id, workspaceId, userId, periodStartUtc, periodEndUtc, cadence);
    }

    public void Submit(DateTimeOffset nowUtc)
    {
        if (Status == ApprovalStatus.Locked)
        {
            throw new ConflictException("This period is locked.");
        }

        Status = ApprovalStatus.Submitted;
        SubmittedAtUtc = nowUtc;
    }

    public void Approve(Guid approverUserId, Guid approvalId, string? comment, DateTimeOffset nowUtc)
    {
        if (Status == ApprovalStatus.Locked)
        {
            throw new ConflictException("This period is locked.");
        }

        Status = ApprovalStatus.Approved;
        ApprovedByUserId = approverUserId;
        DecidedAtUtc = nowUtc;
        _approvals.Add(new TimesheetApproval(approvalId, Id, approverUserId, approved: true, comment, nowUtc));
    }

    public void Reject(Guid approverUserId, Guid approvalId, string? comment, DateTimeOffset nowUtc)
    {
        if (Status == ApprovalStatus.Locked)
        {
            throw new ConflictException("This period is locked.");
        }

        Status = ApprovalStatus.Rejected;
        ApprovedByUserId = null;
        DecidedAtUtc = nowUtc;
        _approvals.Add(new TimesheetApproval(approvalId, Id, approverUserId, approved: false, comment, nowUtc));
    }

    public void Lock(DateTimeOffset nowUtc)
    {
        Status = ApprovalStatus.Locked;
        DecidedAtUtc = nowUtc;
    }
}

/// <summary>A single approve/reject decision on a timesheet period (with an optional comment).</summary>
public sealed class TimesheetApproval : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private TimesheetApproval()
    {
    }

    public TimesheetApproval(Guid id, Guid periodId, Guid approverUserId, bool approved, string? comment, DateTimeOffset createdAtUtc)
        : base(id)
    {
        PeriodId = periodId;
        ApproverUserId = approverUserId;
        Approved = approved;
        Comment = comment;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid PeriodId { get; private set; }
    public Guid ApproverUserId { get; private set; }
    public bool Approved { get; private set; }
    public string? Comment { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
