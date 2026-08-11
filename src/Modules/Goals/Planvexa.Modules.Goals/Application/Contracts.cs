namespace Planvexa.Modules.Goals.Application;

using Planvexa.Modules.Goals.Domain;

// ---- DTOs ----
public sealed record GoalFolderDto(Guid Id, string Name);

public sealed record GoalLinkedTaskDto(Guid TaskId, string? Title, bool? IsCompleted, bool Visible);

public sealed record GoalKeyResultDto(Guid Id, string Title, decimal CurrentValue, decimal TargetValue, GoalUnit Unit, decimal PercentComplete);

public sealed record GoalDto(
    Guid Id, Guid? FolderId, string Name, string? Description, Guid OwnerUserId,
    DateTimeOffset StartDate, DateTimeOffset EndDate, GoalTargetType TargetType,
    decimal? TargetValue, decimal? CurrentValue, GoalUnit Unit, GoalStatus Status, decimal PercentComplete,
    int LinkedTaskCount, int CompletedLinkedTaskCount, int KeyResultCount);

public sealed record GoalDetailDto(GoalDto Goal, IReadOnlyList<GoalLinkedTaskDto> LinkedTasks, IReadOnlyList<GoalKeyResultDto> KeyResults);

public sealed record GoalCommentDto(Guid Id, Guid AuthorUserId, string Body, DateTimeOffset CreatedAtUtc);

// ---- Commands ----
public sealed record CreateGoalCommand(
    Guid? FolderId, string Name, string? Description, Guid? OwnerUserId,
    DateTimeOffset StartDate, DateTimeOffset EndDate, GoalTargetType TargetType,
    decimal? TargetValue, decimal? CurrentValue, GoalUnit Unit = GoalUnit.Number);

public sealed record UpdateGoalCommand(
    string? Name, string? Description, Guid? FolderId, DateTimeOffset? StartDate, DateTimeOffset? EndDate,
    decimal? CurrentValue, GoalStatus? Status);

public sealed record LinkKeyResultCommand(string Title, decimal TargetValue, decimal CurrentValue, GoalUnit Unit);

public sealed record UpdateKeyResultCommand(string? Title, decimal? CurrentValue, decimal? TargetValue, GoalUnit? Unit);
