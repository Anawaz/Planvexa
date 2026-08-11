namespace Planvexa.Modules.TimeTracking.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// A free-form label scoped to a workspace, applied to time entries via <see cref="TimeEntryTag"/>.
/// TimeTracking keeps its own lightweight tag list rather than reusing WorkManagement's task
/// <c>Tag</c>: the two are conceptually the same shape, but reusing it would mean either a new
/// cross-module contract (AGENTS.md rule 7) just to resolve tag names, or letting TimeTracking read
/// WorkManagement's tables directly (not allowed). A time entry's tags are simple free-form labels
/// with no colour/board semantics, so a small dedicated table is the simpler and cheaper option.
/// </summary>
public sealed class TimeTag : Entity, IWorkspaceOwned
{
    private TimeTag()
    {
    }

    private TimeTag(Guid id, Guid workspaceId, string name, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static TimeTag Create(Guid id, Guid workspaceId, string name, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        return new TimeTag(id, workspaceId, name.Trim(), nowUtc);
    }
}

/// <summary>Join row between a <see cref="TimeEntry"/> and a <see cref="TimeTag"/>.</summary>
public sealed class TimeEntryTag : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private TimeEntryTag()
    {
    }

    public TimeEntryTag(Guid id, Guid timeEntryId, Guid tagId)
        : base(id)
    {
        TimeEntryId = timeEntryId;
        TagId = tagId;
    }

    public Guid TimeEntryId { get; private set; }
    public Guid TagId { get; private set; }
}
