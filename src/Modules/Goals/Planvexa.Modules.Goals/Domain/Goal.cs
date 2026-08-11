namespace Planvexa.Modules.Goals.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// Net-new: a workspace-wide OKR/goal. Owns its linked-task links via the aggregate (progress
/// for a <see cref="GoalTargetType.LinkedTasksRatio"/> goal is completed/total among these). Comments live
/// in a separate lightweight aggregate (<see cref="GoalComment"/>) — a goal-scoped comment thread does not
/// need Collaboration's full mention/reaction/share-link machinery, and Goals cannot reference the
/// Collaboration module directly (AGENTS.md rule 7), so wiring a cross-module contract for "post a comment"
/// would be disproportionate to what this needs.
/// </summary>
public sealed class Goal : Entity, IAggregateRoot, IWorkspaceOwned
{
    private readonly List<GoalLinkedTask> _linkedTasks = new();
    private readonly List<GoalKeyResult> _keyResults = new();

    private Goal()
    {
    }

    private Goal(
        Guid id, Guid workspaceId, Guid? folderId, string name, string? description, Guid ownerUserId,
        DateTimeOffset startDate, DateTimeOffset endDate, GoalTargetType targetType,
        decimal? targetValue, decimal? currentValue, GoalUnit unit, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        FolderId = folderId;
        Name = name;
        Description = description;
        OwnerUserId = ownerUserId;
        StartDate = startDate;
        EndDate = endDate;
        TargetType = targetType;
        TargetValue = targetValue;
        CurrentValue = currentValue;
        Unit = unit;
        Status = GoalStatus.NotStarted;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid? FolderId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public DateTimeOffset StartDate { get; private set; }
    public DateTimeOffset EndDate { get; private set; }
    public GoalTargetType TargetType { get; private set; }

    /// <summary>Numeric-target goals only (e.g. "200000" for "$200k ARR"). Null for LinkedTasksRatio goals.</summary>
    public decimal? TargetValue { get; private set; }

    /// <summary>Numeric-target goals only, manually updated by the owner. Null for LinkedTasksRatio goals.</summary>
    public decimal? CurrentValue { get; private set; }

    /// <summary>Display-only formatting for <see cref="TargetValue"/>/<see cref="CurrentValue"/> (Numeric goals
    /// only) — see <see cref="GoalUnit"/>.</summary>
    public GoalUnit Unit { get; private set; }

    public GoalStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyList<GoalLinkedTask> LinkedTasks => _linkedTasks.AsReadOnly();

    /// <summary>Owned key results (OKR-style). When non-empty, these — not <see cref="TargetType"/> — drive
    /// the goal's overall progress (see <see cref="GoalProgressCalculator.PercentComplete"/>).</summary>
    public IReadOnlyList<GoalKeyResult> KeyResults => _keyResults.AsReadOnly();

    public static Goal Create(
        Guid id, Guid workspaceId, Guid? folderId, string name, string? description, Guid ownerUserId,
        DateTimeOffset startDate, DateTimeOffset endDate, GoalTargetType targetType,
        decimal? targetValue, decimal? currentValue, DateTimeOffset nowUtc, GoalUnit unit = GoalUnit.Number)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstEmpty(ownerUserId, nameof(ownerUserId));
        if (endDate < startDate)
        {
            throw new ValidationAppException("A goal's end date must be on or after its start date.");
        }

        ValidateTarget(targetType, targetValue);

        return new Goal(id, workspaceId, folderId, name.Trim(), description?.Trim(), ownerUserId, startDate, endDate, targetType, targetValue, targetType == GoalTargetType.Numeric ? currentValue ?? 0m : null, unit, nowUtc);
    }

    public void Update(
        string? name, string? description, Guid? folderId, DateTimeOffset? startDate, DateTimeOffset? endDate,
        decimal? currentValue, GoalStatus? status, DateTimeOffset nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name.Trim();
        }

        if (description is not null)
        {
            Description = description.Trim();
        }

        FolderId = folderId ?? FolderId;

        var newStart = startDate ?? StartDate;
        var newEnd = endDate ?? EndDate;
        if (newEnd < newStart)
        {
            throw new ValidationAppException("A goal's end date must be on or after its start date.");
        }

        StartDate = newStart;
        EndDate = newEnd;

        if (currentValue is not null)
        {
            if (TargetType != GoalTargetType.Numeric)
            {
                throw new ValidationAppException("Only a numeric-target goal's current value can be set directly.");
            }

            CurrentValue = currentValue;
        }

        if (status is { } s)
        {
            Status = s;
        }

        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Links a task to a LinkedTasksRatio goal (contributes to its completion-ratio progress). A
    /// no-op if already linked. Rejected for Numeric goals — they track progress via <see cref="CurrentValue"/>,
    /// not task links, so a stray link would silently do nothing and confuse the owner.</summary>
    public GoalLinkedTask LinkTask(Guid linkId, Guid taskId, DateTimeOffset nowUtc)
    {
        if (TargetType != GoalTargetType.LinkedTasksRatio)
        {
            throw new ValidationAppException("Only a linked-tasks-ratio goal can have linked tasks.");
        }

        var existing = _linkedTasks.FirstOrDefault(l => l.TaskId == taskId);
        if (existing is not null)
        {
            return existing;
        }

        var link = GoalLinkedTask.Create(linkId, Id, taskId, nowUtc);
        _linkedTasks.Add(link);
        UpdatedAtUtc = nowUtc;
        return link;
    }

    public bool UnlinkTask(Guid taskId, DateTimeOffset nowUtc)
    {
        var existing = _linkedTasks.FirstOrDefault(l => l.TaskId == taskId);
        if (existing is null)
        {
            return false;
        }

        _linkedTasks.Remove(existing);
        UpdatedAtUtc = nowUtc;
        return true;
    }

    /// <summary>Adds an owned key result. Any Goal can own key results regardless of <see cref="TargetType"/> —
    /// once any exist, they drive overall progress instead (see <see cref="GoalProgressCalculator"/>).</summary>
    public GoalKeyResult LinkKeyResult(Guid id, string title, decimal targetValue, decimal currentValue, GoalUnit unit, DateTimeOffset nowUtc)
    {
        var keyResult = GoalKeyResult.Create(id, Id, title, targetValue, currentValue, unit, nowUtc);
        _keyResults.Add(keyResult);
        UpdatedAtUtc = nowUtc;
        return keyResult;
    }

    public GoalKeyResult UpdateKeyResult(Guid keyResultId, string? title, decimal? currentValue, decimal? targetValue, GoalUnit? unit, DateTimeOffset nowUtc)
    {
        var keyResult = _keyResults.FirstOrDefault(k => k.Id == keyResultId)
            ?? throw new NotFoundException("Key result not found.");
        keyResult.Update(title, currentValue, targetValue, unit, nowUtc);
        UpdatedAtUtc = nowUtc;
        return keyResult;
    }

    public bool RemoveKeyResult(Guid keyResultId, DateTimeOffset nowUtc)
    {
        var existing = _keyResults.FirstOrDefault(k => k.Id == keyResultId);
        if (existing is null)
        {
            return false;
        }

        _keyResults.Remove(existing);
        UpdatedAtUtc = nowUtc;
        return true;
    }

    private static void ValidateTarget(GoalTargetType type, decimal? targetValue)
    {
        if (type == GoalTargetType.Numeric && targetValue is null)
        {
            throw new ValidationAppException("A numeric-target goal requires a target value.");
        }

        if (type == GoalTargetType.Numeric && targetValue <= 0)
        {
            throw new ValidationAppException("A numeric goal's target value must be positive.");
        }
    }
}

/// <summary>A task contributing to a LinkedTasksRatio goal's progress.</summary>
public sealed class GoalLinkedTask : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private GoalLinkedTask()
    {
    }

    private GoalLinkedTask(Guid id, Guid goalId, Guid taskId, DateTimeOffset nowUtc)
        : base(id)
    {
        GoalId = goalId;
        TaskId = taskId;
        LinkedAtUtc = nowUtc;
    }

    public Guid GoalId { get; private set; }
    public Guid TaskId { get; private set; }
    public DateTimeOffset LinkedAtUtc { get; private set; }

    public static GoalLinkedTask Create(Guid id, Guid goalId, Guid taskId, DateTimeOffset nowUtc)
        => new(id, goalId, taskId, nowUtc);
}

/// <summary>An OKR-style key result owned by a Goal (child entity, mirrors <see cref="GoalLinkedTask"/>'s
/// persistence/EF-configuration pattern). Tracks its own current/target value; a Goal's overall progress is
/// the average of its key results' completion percentages once it has any (see
/// <see cref="GoalProgressCalculator"/>).</summary>
public sealed class GoalKeyResult : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private GoalKeyResult()
    {
    }

    private GoalKeyResult(Guid id, Guid goalId, string title, decimal targetValue, decimal currentValue, GoalUnit unit, DateTimeOffset nowUtc)
        : base(id)
    {
        GoalId = goalId;
        Title = title;
        TargetValue = targetValue;
        CurrentValue = currentValue;
        Unit = unit;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid GoalId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public decimal CurrentValue { get; private set; }
    public decimal TargetValue { get; private set; }
    public GoalUnit Unit { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static GoalKeyResult Create(Guid id, Guid goalId, string title, decimal targetValue, decimal currentValue, GoalUnit unit, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(title, nameof(title));
        if (targetValue <= 0)
        {
            throw new ValidationAppException("A key result's target value must be positive.");
        }

        return new GoalKeyResult(id, goalId, title.Trim(), targetValue, currentValue, unit, nowUtc);
    }

    public void Update(string? title, decimal? currentValue, decimal? targetValue, GoalUnit? unit, DateTimeOffset nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title.Trim();
        }

        if (targetValue is { } t)
        {
            if (t <= 0)
            {
                throw new ValidationAppException("A key result's target value must be positive.");
            }

            TargetValue = t;
        }

        if (currentValue is { } c)
        {
            CurrentValue = c;
        }

        if (unit is { } u)
        {
            Unit = u;
        }

        UpdatedAtUtc = nowUtc;
    }
}

/// <summary>Pure progress math for a Goal, split out so it is unit-testable without a database (mirrors
/// RollupAggregator/BudgetCalculator's split). <paramref name="completedLinkedTasks"/>/<paramref name="totalLinkedTasks"/>
/// are the UNFILTERED counts (the ratio itself does not leak any task's title/data — only the linked-tasks
/// LIST view needs permission filtering, see IWorkReportingQueries.ReadableTaskCardsAsync).</summary>
public static class GoalProgressCalculator
{
    /// <summary>When the goal owns any key results, they drive overall progress (a plain average of each key
    /// result's own completion percentage) regardless of <see cref="Goal.TargetType"/>; otherwise falls back
    /// to the existing Numeric/LinkedTasksRatio calculation.</summary>
    public static decimal PercentComplete(Goal goal, int completedLinkedTasks, int totalLinkedTasks)
    {
        if (goal.KeyResults.Count > 0)
        {
            return KeyResultsAverage(goal.KeyResults);
        }

        return goal.TargetType switch
        {
            GoalTargetType.Numeric => NumericPercent(goal.CurrentValue ?? 0m, goal.TargetValue ?? 0m),
            GoalTargetType.LinkedTasksRatio => LinkedTasksPercent(completedLinkedTasks, totalLinkedTasks),
            _ => 0m,
        };
    }

    public static decimal KeyResultsAverage(IReadOnlyList<GoalKeyResult> keyResults)
        => keyResults.Count == 0 ? 0m : Math.Round(keyResults.Average(k => NumericPercent(k.CurrentValue, k.TargetValue)), 1, MidpointRounding.AwayFromZero);

    public static decimal NumericPercent(decimal current, decimal target)
        => target <= 0 ? 0m : Math.Clamp(Math.Round(current / target * 100m, 1, MidpointRounding.AwayFromZero), 0m, 999m);

    public static decimal LinkedTasksPercent(int completed, int total)
        => total <= 0 ? 0m : Math.Round(completed * 100m / total, 1, MidpointRounding.AwayFromZero);
}
