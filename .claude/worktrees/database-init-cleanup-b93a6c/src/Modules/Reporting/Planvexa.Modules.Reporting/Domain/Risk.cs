namespace Planvexa.Modules.Reporting.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

public enum RiskSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}

public enum RiskStatus
{
    Open = 0,
    Mitigating = 1,
    Resolved = 2,
    Accepted = 3,
}

/// <summary>What a Risk is linked to. <see cref="ScopeId"/> is a WorkManagement Space/List id or a Goals
/// Goal id, resolved by id only (no FK — AGENTS.md rule 7, modules integrate through contracts/ids).</summary>
public enum RiskScopeType
{
    Space = 0,
    List = 1,
    Goal = 2,
}

/// <summary>Net-new: a portfolio risk register entry, surfaced in PortfolioService's output
/// alongside Milestones and Budget status.</summary>
public sealed class Risk : Entity, IAggregateRoot, IWorkspaceOwned
{
    private Risk()
    {
    }

    private Risk(
        Guid id, Guid workspaceId, string title, string? description, RiskSeverity severity,
        RiskScopeType scopeType, Guid scopeId, RiskStatus status, Guid createdByUserId, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Title = title;
        Description = description;
        Severity = severity;
        ScopeType = scopeType;
        ScopeId = scopeId;
        Status = status;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public RiskSeverity Severity { get; private set; }
    public RiskScopeType ScopeType { get; private set; }
    public Guid ScopeId { get; private set; }
    public RiskStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Risk Create(
        Guid id, Guid workspaceId, string title, string? description, RiskSeverity severity,
        RiskScopeType scopeType, Guid scopeId, Guid createdByUserId, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(title, nameof(title));
        Guard.AgainstEmpty(scopeId, nameof(scopeId));
        return new Risk(id, workspaceId, title.Trim(), description?.Trim(), severity, scopeType, scopeId, RiskStatus.Open, createdByUserId, nowUtc);
    }

    public void Update(string? title, string? description, RiskSeverity? severity, RiskStatus? status, DateTimeOffset nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title.Trim();
        }

        if (description is not null)
        {
            Description = description.Trim();
        }

        if (severity is { } s)
        {
            Severity = s;
        }

        if (status is { } st)
        {
            Status = st;
        }

        UpdatedAtUtc = nowUtc;
    }
}
