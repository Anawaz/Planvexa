namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// Defines a recurring task series. Generation is idempotent (ADR-0009): each occurrence has a
/// deterministic key <c>{DefinitionId}:{occurrence-local-date}</c> recorded in a dedup table with a
/// unique constraint, so retries and concurrent runs never create duplicates.
/// </summary>
public sealed class RecurringTaskDefinition : Entity, IWorkspaceOwned
{
    private RecurringTaskDefinition()
    {
    }

    private RecurringTaskDefinition(
        Guid id, Guid workspaceId, Guid listId, string title,
        RecurrenceFrequency frequency, int interval, string timeZoneId, DateTimeOffset anchorUtc, Guid createdBy)
        : base(id)
    {
        WorkspaceId = workspaceId;
        ListId = listId;
        Title = title;
        Frequency = frequency;
        Interval = interval;
        TimeZoneId = timeZoneId;
        AnchorUtc = anchorUtc;
        NextRunUtc = anchorUtc;
        CreatedByUserId = createdBy;
        IsActive = true;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid ListId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public TaskPriority Priority { get; private set; }
    public RecurrenceFrequency Frequency { get; private set; }

    /// <summary>Every N frequency units (e.g. every 2 weeks).</summary>
    public int Interval { get; private set; }

    public string TimeZoneId { get; private set; } = "UTC";
    public DateTimeOffset AnchorUtc { get; private set; }
    public DateTimeOffset NextRunUtc { get; private set; }
    public DateTimeOffset? LastGeneratedUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public bool IsActive { get; private set; }

    public static RecurringTaskDefinition Create(
        Guid id, Guid workspaceId, Guid listId, string title,
        RecurrenceFrequency frequency, int interval, string timeZoneId, DateTimeOffset anchorUtc, Guid createdBy)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(listId, nameof(listId));
        Guard.AgainstNullOrWhiteSpace(title, nameof(title));
        if (interval < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be at least 1.");
        }

        return new RecurringTaskDefinition(id, workspaceId, listId, title.Trim(), frequency, interval, timeZoneId, anchorUtc, createdBy);
    }

    public void SetDetails(string? description, TaskPriority priority)
    {
        Description = description;
        Priority = priority;
    }

    /// <summary>Deterministic dedup key for an occurrence (stable across retries/workers).</summary>
    public string OccurrenceKey(DateTimeOffset occurrenceUtc)
        => $"{Id:N}:{occurrenceUtc.UtcDateTime:yyyyMMddHHmm}";

    /// <summary>Advances <see cref="NextRunUtc"/> by one interval from the given occurrence.</summary>
    public void AdvanceAfter(DateTimeOffset occurrenceUtc, DateTimeOffset nowUtc)
    {
        NextRunUtc = Recurrence.Next(occurrenceUtc, Frequency, Interval);
        LastGeneratedUtc = nowUtc;
    }

    public void Deactivate() => IsActive = false;
}

/// <summary>Records that a specific occurrence has been generated (uniqueness enforces idempotency).</summary>
public sealed class RecurringOccurrence : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private RecurringOccurrence()
    {
    }

    public RecurringOccurrence(Guid id, Guid definitionId, string occurrenceKey, Guid generatedTaskId, DateTimeOffset generatedAtUtc)
        : base(id)
    {
        DefinitionId = definitionId;
        OccurrenceKey = occurrenceKey;
        GeneratedTaskId = generatedTaskId;
        GeneratedAtUtc = generatedAtUtc;
    }

    public Guid DefinitionId { get; private set; }
    public string OccurrenceKey { get; private set; } = string.Empty;
    public Guid GeneratedTaskId { get; private set; }
    public DateTimeOffset GeneratedAtUtc { get; private set; }
}

/// <summary>Pure recurrence arithmetic (unit-tested, timezone-aware for local calendar rules).</summary>
public static class Recurrence
{
    public static DateTimeOffset Next(DateTimeOffset from, RecurrenceFrequency frequency, int interval) => frequency switch
    {
        RecurrenceFrequency.Daily => from.AddDays(interval),
        RecurrenceFrequency.Weekly => from.AddDays(7 * interval),
        RecurrenceFrequency.Monthly => from.AddMonths(interval),
        RecurrenceFrequency.Yearly => from.AddYears(interval),
        _ => from.AddDays(interval),
    };
}
