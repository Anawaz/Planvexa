namespace Planvexa.Modules.Goals.Domain;

/// <summary>How a Goal's progress is measured. <see cref="Numeric"/> is the classic OKR "current/target"
/// style (e.g. "$50k / $200k ARR"); <see cref="LinkedTasksRatio"/> is task-completion style (progress =
/// completed / total among the Goal's <see cref="GoalLinkedTask"/> links).</summary>
public enum GoalTargetType
{
    Numeric = 0,
    LinkedTasksRatio = 1,
}

public enum GoalStatus
{
    NotStarted = 0,
    OnTrack = 1,
    AtRisk = 2,
    OffTrack = 3,
    Completed = 4,
    Archived = 5,
}
