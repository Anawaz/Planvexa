namespace Planvexa.Modules.Planning.Application;

using Planvexa.Modules.Planning.Domain;

// ---- DTOs ----
public sealed record WorkScheduleDto(IReadOnlyList<int> WorkingDays, decimal DailyCapacityHours);

public sealed record HolidayDto(Guid Id, DateTimeOffset DateUtc, string Name);

public sealed record LeaveDto(Guid Id, Guid UserId, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string Type);

public sealed record EstimateDto(Guid TaskId, long EstimateSeconds);

public sealed record SprintDto(Guid Id, string Name, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string Status, int TotalPoints, string? Goal);

public sealed record SprintItemDto(Guid TaskId, int? Points);

public sealed record SprintBoardColumnDto(Guid StatusId, string StatusName, IReadOnlyList<SprintBoardCardDto> Tasks);

public sealed record SprintBoardCardDto(Guid Id, string Title, int? Points);

public sealed record SprintBoardDto(Guid SprintId, string Name, IReadOnlyList<SprintBoardColumnDto> Columns);

public sealed record WorkloadRowDto(Guid UserId, decimal CapacityHours, decimal ScheduledHours, decimal LoggedHours, bool IsOverAllocated);

// ---- Commands ----
public sealed record UpdateWorkScheduleCommand(IReadOnlyList<int> WorkingDays, decimal DailyCapacityHours);

public sealed record AddHolidayCommand(DateTimeOffset DateUtc, string Name);

public sealed record AddLeaveCommand(Guid? UserId, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string Type);

public sealed record CreateSprintCommand(string Name, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string? Goal = null);

public sealed record UpdateSprintCommand(string? Name, DateTimeOffset? StartUtc, DateTimeOffset? EndUtc, string? Goal);

public sealed record AddSprintItemCommand(Guid TaskId, int? Points);

public sealed record ChangeSprintStatusCommand(SprintStatus Status);
