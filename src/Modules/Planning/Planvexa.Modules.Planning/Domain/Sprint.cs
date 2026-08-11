namespace Planvexa.Modules.Planning.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>A time-boxed sprint (iteration) in a workspace. Owns its items via the aggregate.</summary>
public sealed class Sprint : Entity, IAggregateRoot, IWorkspaceOwned
{
    private readonly List<SprintItem> _items = new();

    private Sprint()
    {
    }

    private Sprint(Guid id, Guid workspaceId, string name, DateTime startDate, DateTime endDate, Guid createdBy, DateTimeOffset nowUtc, string? goal)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        StartDate = startDate;
        EndDate = endDate;
        Status = SprintStatus.Planned;
        CreatedByUserId = createdBy;
        CreatedAtUtc = nowUtc;
        Goal = string.IsNullOrWhiteSpace(goal) ? null : goal.Trim();
    }

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }
    public SprintStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string? Goal { get; private set; }

    public IReadOnlyList<SprintItem> Items => _items.AsReadOnly();

    public static Sprint Create(Guid id, Guid workspaceId, string name, DateTimeOffset startUtc, DateTimeOffset endUtc, Guid createdBy, DateTimeOffset nowUtc, string? goal = null)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        var start = startUtc.UtcDateTime.Date;
        var end = endUtc.UtcDateTime.Date;
        if (end < start)
        {
            throw new ValidationAppException("Sprint end date must be on or after the start date.");
        }

        return new Sprint(id, workspaceId, name.Trim(), start, end, createdBy, nowUtc, goal);
    }

    public void Rename(string name)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Name = name.Trim();
    }

    public void SetGoal(string? goal)
    {
        Goal = string.IsNullOrWhiteSpace(goal) ? null : goal.Trim();
    }

    public void SetSchedule(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var start = startUtc.UtcDateTime.Date;
        var end = endUtc.UtcDateTime.Date;
        if (end < start)
        {
            throw new ValidationAppException("Sprint end date must be on or after the start date.");
        }

        StartDate = start;
        EndDate = end;
    }

    public void ChangeStatus(SprintStatus status)
    {
        var isValidTransition = (Status, status) switch
        {
            (SprintStatus.Planned, SprintStatus.Active) => true,
            (SprintStatus.Active, SprintStatus.Completed) => true,
            _ => false,
        };

        if (!isValidTransition)
        {
            throw new ConflictException($"Cannot change sprint status from {Status} to {status}.");
        }

        Status = status;
    }

    public SprintItem AddItem(Guid itemId, Guid taskId, int? points)
    {
        Guard.AgainstEmpty(taskId, nameof(taskId));
        var existing = _items.FirstOrDefault(i => i.TaskId == taskId);
        if (existing is not null)
        {
            existing.SetPoints(points);
            return existing;
        }

        if (points is < 0)
        {
            throw new ValidationAppException("Sprint points cannot be negative.");
        }

        var item = SprintItem.Create(itemId, Id, taskId, points);
        _items.Add(item);
        return item;
    }

    public bool RemoveItem(Guid taskId)
    {
        var existing = _items.FirstOrDefault(i => i.TaskId == taskId);
        if (existing is null)
        {
            return false;
        }

        _items.Remove(existing);
        return true;
    }

    public int TotalPoints() => _items.Sum(i => i.Points ?? 0);
}

/// <summary>A task assigned to a sprint, with optional story points.</summary>
public sealed class SprintItem : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private SprintItem()
    {
    }

    private SprintItem(Guid id, Guid sprintId, Guid taskId, int? points)
        : base(id)
    {
        SprintId = sprintId;
        TaskId = taskId;
        Points = points;
    }

    public Guid SprintId { get; private set; }
    public Guid TaskId { get; private set; }
    public int? Points { get; private set; }

    public static SprintItem Create(Guid id, Guid sprintId, Guid taskId, int? points)
        => new(id, sprintId, taskId, points);

    public void SetPoints(int? points)
    {
        if (points is < 0)
        {
            throw new ValidationAppException("Sprint points cannot be negative.");
        }

        Points = points;
    }
}
