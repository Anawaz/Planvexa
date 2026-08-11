namespace Planvexa.Modules.Planning.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// The working calendar for a workspace: which days are working days and the standard daily capacity.
/// Working days are stored as ISO day numbers (1=Monday … 7=Sunday).
/// </summary>
public sealed class WorkSchedule : Entity, IWorkspaceOwned
{
    private WorkSchedule()
    {
    }

    private WorkSchedule(Guid id, Guid workspaceId, int workingDaysMask, decimal dailyCapacityHours)
        : base(id)
    {
        WorkspaceId = workspaceId;
        WorkingDaysMask = workingDaysMask;
        DailyCapacityHours = dailyCapacityHours;
    }

    public Guid WorkspaceId { get; private set; }

    /// <summary>Bitmask of working ISO days; bit (day-1) set means that day is a working day.</summary>
    public int WorkingDaysMask { get; private set; }

    public decimal DailyCapacityHours { get; private set; }

    public static WorkSchedule CreateDefault(Guid id, Guid workspaceId)
    {
        // Monday–Friday, 8h/day.
        var mask = MaskFromDays(new[] { 1, 2, 3, 4, 5 });
        return new WorkSchedule(id, workspaceId, mask, 8m);
    }

    public void Update(IReadOnlyCollection<int> workingDays, decimal dailyCapacityHours)
    {
        WorkingDaysMask = MaskFromDays(workingDays);
        DailyCapacityHours = dailyCapacityHours < 0 ? 0 : dailyCapacityHours;
    }

    public bool IsWorkingDay(DayOfWeek day)
    {
        var iso = day == DayOfWeek.Sunday ? 7 : (int)day; // Mon=1..Sun=7
        return (WorkingDaysMask & (1 << (iso - 1))) != 0;
    }

    public IReadOnlyList<int> WorkingDays()
    {
        var days = new List<int>();
        for (var iso = 1; iso <= 7; iso++)
        {
            if ((WorkingDaysMask & (1 << (iso - 1))) != 0)
            {
                days.Add(iso);
            }
        }

        return days;
    }

    private static int MaskFromDays(IReadOnlyCollection<int> workingDays)
    {
        var mask = 0;
        foreach (var day in workingDays)
        {
            if (day is >= 1 and <= 7)
            {
                mask |= 1 << (day - 1);
            }
        }

        return mask;
    }
}

/// <summary>A non-working holiday in a workspace calendar (date is a calendar date at UTC midnight).</summary>
public sealed class Holiday : Entity, IWorkspaceOwned
{
    private Holiday()
    {
    }

    private Holiday(Guid id, Guid workspaceId, DateTimeOffset dateUtc, string name)
        : base(id)
    {
        WorkspaceId = workspaceId;
        DateUtc = dateUtc.UtcDateTime.Date;
        Name = name;
    }

    public Guid WorkspaceId { get; private set; }
    public DateTime DateUtc { get; private set; }
    public string Name { get; private set; } = string.Empty;

    public static Holiday Create(Guid id, Guid workspaceId, DateTimeOffset dateUtc, string name)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        return new Holiday(id, workspaceId, dateUtc, name.Trim());
    }
}

/// <summary>A user's leave (time off) that reduces their available capacity.</summary>
public sealed class LeaveEntry : Entity, IWorkspaceOwned
{
    private LeaveEntry()
    {
    }

    private LeaveEntry(Guid id, Guid workspaceId, Guid userId, DateTime startDate, DateTime endDate, LeaveType type)
        : base(id)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        StartDate = startDate;
        EndDate = endDate;
        Type = type;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>Inclusive start calendar date (UTC).</summary>
    public DateTime StartDate { get; private set; }

    /// <summary>Inclusive end calendar date (UTC).</summary>
    public DateTime EndDate { get; private set; }

    public LeaveType Type { get; private set; }

    public static LeaveEntry Create(Guid id, Guid workspaceId, Guid userId, DateTimeOffset startUtc, DateTimeOffset endUtc, LeaveType type)
    {
        Guard.AgainstEmpty(userId, nameof(userId));
        var start = startUtc.UtcDateTime.Date;
        var end = endUtc.UtcDateTime.Date;
        if (end < start)
        {
            throw new BuildingBlocks.Exceptions.ValidationAppException("Leave end date must be on or after the start date.");
        }

        return new LeaveEntry(id, workspaceId, userId, start, end, type);
    }

    public bool Covers(DateTime date) => date >= StartDate && date <= EndDate;
}
