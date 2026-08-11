using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// A workspace's data-retention policy (one per workspace). Controls how long soft-deleted tasks and audit
/// events are kept, and a legal-hold flag that blocks all automated purging. A retention window of 0
/// means "keep forever". Time is UTC.
/// </summary>
public sealed class RetentionPolicy : Entity, IWorkspaceOwned
{
    private RetentionPolicy()
    {
    }

    private RetentionPolicy(Guid id, Guid workspaceId, int deletedTaskRetentionDays, int auditRetentionDays, bool legalHold, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        DeletedTaskRetentionDays = deletedTaskRetentionDays;
        AuditRetentionDays = auditRetentionDays;
        LegalHold = legalHold;
        UpdatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }

    /// <summary>Days to retain soft-deleted tasks before purge. 0 = keep forever.</summary>
    public int DeletedTaskRetentionDays { get; private set; }

    /// <summary>Days to retain audit events. 0 = keep forever.</summary>
    public int AuditRetentionDays { get; private set; }

    /// <summary>When true, no automated purge occurs (legal/compliance hold).</summary>
    public bool LegalHold { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static RetentionPolicy CreateDefault(Guid id, Guid workspaceId, DateTimeOffset nowUtc)
        => new(id, workspaceId, deletedTaskRetentionDays: 0, auditRetentionDays: 0, legalHold: false, nowUtc);

    public void Update(int? deletedTaskRetentionDays, int? auditRetentionDays, bool? legalHold, DateTimeOffset nowUtc)
    {
        if (deletedTaskRetentionDays is { } d)
        {
            if (d < 0)
            {
                throw new ValidationAppException("Retention days cannot be negative.");
            }

            DeletedTaskRetentionDays = d;
        }

        if (auditRetentionDays is { } a)
        {
            if (a < 0)
            {
                throw new ValidationAppException("Retention days cannot be negative.");
            }

            AuditRetentionDays = a;
        }

        if (legalHold is { } hold)
        {
            LegalHold = hold;
        }

        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// The cutoff instant before which soft-deleted tasks may be purged, or null when purging is disabled
    /// (legal hold, or a zero/keep-forever window). Pure — safe to unit-test.
    /// </summary>
    public DateTimeOffset? PurgeCutoff(DateTimeOffset nowUtc)
        => LegalHold || DeletedTaskRetentionDays <= 0 ? null : nowUtc.AddDays(-DeletedTaskRetentionDays);
}
