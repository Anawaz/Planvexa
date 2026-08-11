namespace Planvexa.Modules.Reporting.Application;

using Planvexa.Modules.Reporting.Domain;

// ---- DTOs ----
public sealed record WidgetDto(Guid Id, string Type, string ConfigJson, int Position);

public sealed record DashboardDto(Guid Id, string Name, bool IsPrivate, Guid OwnerUserId, IReadOnlyList<WidgetDto> Widgets);

public sealed record DashboardSummaryDto(Guid Id, string Name, bool IsPrivate, Guid OwnerUserId, int WidgetCount);

public sealed record SeriesPointDto(string Label, decimal Value);

public sealed record WidgetDataDto(Guid WidgetId, string Type, IReadOnlyList<SeriesPointDto> Series);

public sealed record MilestoneDto(Guid TaskId, string Title, DateTimeOffset? DueDate, bool IsCompleted);

public sealed record RiskDto(Guid Id, string Title, string? Description, RiskSeverity Severity, RiskScopeType ScopeType, Guid ScopeId, RiskStatus Status);

public sealed record BudgetStatusDto(decimal? MonetaryCapAmount, long? TimeCapSeconds, decimal Hours, decimal Cost, decimal? MonetaryConsumedPercent, decimal? TimeConsumedPercent);

public sealed record PortfolioRowDto(
    string Key, string Label, int TotalTasks, int CompletedTasks, decimal LoggedHours, decimal HealthPercent,
    IReadOnlyList<MilestoneDto> Milestones, IReadOnlyList<RiskDto> Risks, BudgetStatusDto? Budget);

public sealed record DrillDownTaskDto(Guid TaskId, string Title, string StatusName, bool IsCompleted);

public sealed record ScheduledReportDto(Guid Id, Guid DashboardId, IReadOnlyList<string> Recipients, ScheduledReportCadence Cadence, bool IsEnabled, DateTimeOffset? LastSentAtUtc);

// ---- Commands ----
public sealed record WidgetInput(string Type, string? ConfigJson, int Position);

public sealed record CreateDashboardCommand(string Name, bool IsPrivate, IReadOnlyList<WidgetInput> Widgets);

public sealed record UpdateDashboardCommand(string? Name, bool? IsPrivate, IReadOnlyList<WidgetInput>? Widgets);

public sealed record CreateRiskCommand(string Title, string? Description, RiskSeverity Severity, RiskScopeType ScopeType, Guid ScopeId);

public sealed record UpdateRiskCommand(string? Title, string? Description, RiskSeverity? Severity, RiskStatus? Status);

public sealed record CreateScheduledReportCommand(Guid DashboardId, IReadOnlyCollection<string> Recipients, ScheduledReportCadence Cadence);
