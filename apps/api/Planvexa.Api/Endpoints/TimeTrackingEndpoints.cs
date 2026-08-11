namespace Planvexa.Api.Endpoints;

using FluentValidation;
using Planvexa.Modules.TimeTracking.Application;
using Planvexa.Modules.TimeTracking.Application.Services;
using Planvexa.Modules.TimeTracking.Domain;

// ---- Request models ----
public sealed record StartTimerRequest(Guid? TaskId, string? Description, bool? IsBillable, IReadOnlyCollection<Guid>? TagIds = null);
public sealed record StopTimerRequest(string? Description);
public sealed record CreateTimeEntryRequest(Guid? TaskId, DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc, long? DurationSeconds, string? Description, bool? IsBillable, string? TimeZoneId, IReadOnlyCollection<Guid>? TagIds = null);
public sealed record UpdateTimeEntryRequest(DateTimeOffset? StartedAtUtc, DateTimeOffset? EndedAtUtc, string? Description, bool? IsBillable, string? Reason, IReadOnlyCollection<Guid>? TagIds = null);
public sealed record SplitEntryRequest(DateTimeOffset AtUtc, string? Reason);
public sealed record MoveEntryRequest(Guid? TaskId, string? Reason);
public sealed record UpdateTimePolicyRequest(
    bool SingleActiveTimer, int RoundingMinutes, long MinimumDurationSeconds, long MaximumEntrySeconds,
    bool BillableByDefault, bool RequireDescription, bool RequireTask, int EditWindowHours,
    bool ApprovalRequired, int WeekStartsOn, DateTimeOffset? LockDateUtc, long OvertimeThresholdSeconds,
    bool MissingTimeReminderEnabled = false, MissingTimeReminderCadence MissingTimeReminderCadence = MissingTimeReminderCadence.Daily,
    long MissingTimeReminderMinimumSeconds = 0);
public sealed record SetRateRequest(decimal BillingRate, decimal CostRate);
public sealed record SubmitTimesheetRequest(DateTimeOffset WeekStartUtc);
public sealed record RejectTimesheetRequest(string? Comment);
public sealed record CreateTimeTagRequest(string Name);
public sealed record CreateBudgetRequest(BudgetScopeType ScopeType, Guid ScopeId, string Name, decimal? MonetaryCapAmount, long? TimeCapSeconds);
public sealed record UpdateBudgetRequest(string Name, decimal? MonetaryCapAmount, long? TimeCapSeconds);

public sealed class CreateTimeEntryRequestValidator : AbstractValidator<CreateTimeEntryRequest>
{
    public CreateTimeEntryRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.EndedAtUtc is not null || x.DurationSeconds is > 0)
            .WithMessage("Provide either an end time or a positive duration.");
    }
}

public sealed class SetRateRequestValidator : AbstractValidator<SetRateRequest>
{
    public SetRateRequestValidator()
    {
        RuleFor(x => x.BillingRate).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CostRate).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateTimeTagRequestValidator : AbstractValidator<CreateTimeTagRequest>
{
    public CreateTimeTagRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public sealed class CreateBudgetRequestValidator : AbstractValidator<CreateBudgetRequest>
{
    public CreateBudgetRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ScopeId).NotEmpty();
        RuleFor(x => x)
            .Must(x => x.MonetaryCapAmount is not null || x.TimeCapSeconds is not null)
            .WithMessage("Provide a monetary cap, a time cap, or both.");
    }
}

/// <summary>Time-tracking, timesheet and reporting endpoints.</summary>
public static class TimeTrackingEndpoints
{
    public static void MapTimeTrackingEndpoints(this RouteGroupBuilder api)
    {
        MapTimers(api);
        MapEntries(api);
        MapPolicyAndRates(api);
        MapTimesheets(api);
        MapReports(api);
        MapTags(api);
        MapBudgets(api);
    }

    private static void MapTimers(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/timers").RequireAuthorization();

        group.MapPost("/start", async (StartTimerRequest r, HttpContext http, TimeEntryService svc, CancellationToken ct) =>
        {
            var dto = await svc.StartTimerAsync(new StartTimerCommand(r.TaskId, r.Description, r.IsBillable, r.TagIds), IdempotencyKey(http), ct);
            return Results.Created($"/api/v1/time-entries/{dto.Id}", dto);
        });

        group.MapPost("/stop", async (StopTimerRequest r, TimeEntryService svc, CancellationToken ct) =>
            Results.Ok(await svc.StopTimerAsync(new StopTimerCommand(r.Description), ct)));

        group.MapPost("/pause", async (TimeEntryService svc, CancellationToken ct) =>
            Results.Ok(await svc.PauseTimerAsync(ct)));

        group.MapPost("/resume", async (TimeEntryService svc, CancellationToken ct) =>
            Results.Ok(await svc.ResumeTimerAsync(ct)));

        group.MapGet("/active", async (TimeEntryService svc, CancellationToken ct) =>
        {
            var dto = await svc.GetActiveTimerAsync(ct);
            return dto is null ? Results.Ok(new { active = (object?)null }) : Results.Ok(dto);
        });
    }

    private static void MapEntries(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/time-entries").RequireAuthorization();

        group.MapPost("/", async (CreateTimeEntryRequest r, TimeEntryService svc, CancellationToken ct) =>
            {
                var dto = await svc.CreateManualAsync(new CreateManualEntryCommand(
                    r.TaskId, r.StartedAtUtc, r.EndedAtUtc, r.DurationSeconds, r.Description, r.IsBillable, r.TimeZoneId, r.TagIds), ct);
                return Results.Created($"/api/v1/time-entries/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateTimeEntryRequest>>();

        group.MapGet("/", async (DateTimeOffset? from, DateTimeOffset? to, Guid? userId, Guid? taskId, Guid? tagId, TimeEntryService svc, CancellationToken ct) =>
            Results.Ok(await svc.QueryAsync(from, to, userId, taskId, tagId, ct)));

        group.MapPatch("/{id:guid}", async (Guid id, UpdateTimeEntryRequest r, TimeEntryService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(id, new UpdateEntryCommand(r.StartedAtUtc, r.EndedAtUtc, r.Description, r.IsBillable, r.Reason, r.TagIds), ct)));

        group.MapPost("/{id:guid}/split", async (Guid id, SplitEntryRequest r, TimeEntryService svc, CancellationToken ct) =>
            Results.Ok(await svc.SplitAsync(id, r.AtUtc, r.Reason, ct)));

        group.MapPost("/{id:guid}/move", async (Guid id, MoveEntryRequest r, TimeEntryService svc, CancellationToken ct) =>
            Results.Ok(await svc.MoveAsync(id, r.TaskId, r.Reason, ct)));

        group.MapDelete("/{id:guid}", async (Guid id, string? reason, TimeEntryService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, reason, ct);
            return Results.NoContent();
        });
    }

    private static void MapPolicyAndRates(RouteGroupBuilder api)
    {
        api.MapGet("/time-policy", async (TimePolicyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(ct))).RequireAuthorization();

        api.MapPut("/time-policy", async (UpdateTimePolicyRequest r, TimePolicyService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(new UpdatePolicyCommand(
                r.SingleActiveTimer, r.RoundingMinutes, r.MinimumDurationSeconds, r.MaximumEntrySeconds,
                r.BillableByDefault, r.RequireDescription, r.RequireTask, r.EditWindowHours,
                r.ApprovalRequired, r.WeekStartsOn, r.LockDateUtc, r.OvertimeThresholdSeconds,
                r.MissingTimeReminderEnabled, r.MissingTimeReminderCadence, r.MissingTimeReminderMinimumSeconds), ct))).RequireAuthorization();

        api.MapGet("/rates", async (TimePolicyService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListRatesAsync(ct))).RequireAuthorization();

        api.MapPut("/rates/user/{userId:guid}", async (Guid userId, SetRateRequest r, TimePolicyService svc, CancellationToken ct) =>
                Results.Ok(await svc.SetUserRateAsync(userId, new SetRateCommand(r.BillingRate, r.CostRate), ct)))
            .AddEndpointFilter<ValidationFilter<SetRateRequest>>()
            .RequireAuthorization();
    }

    private static void MapTimesheets(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/timesheets").RequireAuthorization();

        group.MapGet("/", async (DateTimeOffset weekStart, Guid? userId, Guid? tagId, TimesheetService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetWeekAsync(weekStart, userId, tagId, ct)));

        group.MapPost("/submit", async (SubmitTimesheetRequest r, TimesheetService svc, CancellationToken ct) =>
            Results.Ok(await svc.SubmitAsync(r.WeekStartUtc, ct)));

        group.MapPost("/{id:guid}/approve", async (Guid id, TimesheetService svc, CancellationToken ct) =>
            Results.Ok(await svc.ApproveAsync(id, null, approve: true, ct)));

        group.MapPost("/{id:guid}/reject", async (Guid id, RejectTimesheetRequest r, TimesheetService svc, CancellationToken ct) =>
            Results.Ok(await svc.ApproveAsync(id, r.Comment, approve: false, ct)));

        group.MapPost("/{id:guid}/lock", async (Guid id, TimesheetService svc, CancellationToken ct) =>
            Results.Ok(await svc.LockAsync(id, ct)));

        group.MapPost("/{id:guid}/reopen", async (Guid id, TimesheetService svc, CancellationToken ct) =>
            Results.Ok(await svc.ReopenAsync(id, ct)));
    }

    private static void MapReports(RouteGroupBuilder api)
    {
        api.MapGet("/reports/time", async (string? groupBy, DateTimeOffset from, DateTimeOffset to, Guid? tagId, TimeReportService svc, CancellationToken ct) =>
            Results.Ok(await svc.ReportAsync(ParseGrouping(groupBy), from, to, tagId, ct))).RequireAuthorization();

        api.MapGet("/reports/time/export", async (string? groupBy, DateTimeOffset from, DateTimeOffset to, Guid? tagId, TimeReportService svc, CancellationToken ct) =>
        {
            var csv = await svc.ExportCsvAsync(ParseGrouping(groupBy), from, to, tagId, ct);
            return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "time-report.csv");
        }).RequireAuthorization();

        // Structured accounting export (QuickBooks Online "Transaction Pro Importer" time-
        // activity CSV layout -- see TimeReportService.ExportAccountingCsvAsync). Admin+ gated by the
        // service itself, same as the generic export above.
        api.MapGet("/reports/time/export/accounting", async (DateTimeOffset from, DateTimeOffset to, Guid? tagId, TimeReportService svc, CancellationToken ct) =>
        {
            var csv = await svc.ExportAccountingCsvAsync(from, to, tagId, ct);
            return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "time-accounting-export.csv");
        }).RequireAuthorization();

        api.MapGet("/reports/utilization", async (DateTimeOffset from, DateTimeOffset to, TimeReportService svc, CancellationToken ct) =>
            Results.Ok(await svc.UtilizationAsync(from, to, ct))).RequireAuthorization();
    }

    private static void MapTags(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/time-tags").RequireAuthorization();

        group.MapGet("/", async (TimeTagService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateTimeTagRequest r, TimeTagService svc, CancellationToken ct) =>
                Results.Ok(await svc.CreateAsync(new CreateTimeTagCommand(r.Name), ct)))
            .AddEndpointFilter<ValidationFilter<CreateTimeTagRequest>>();
    }

    private static void MapBudgets(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/budgets").RequireAuthorization();

        group.MapGet("/", async (BudgetService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateBudgetRequest r, BudgetService svc, CancellationToken ct) =>
            {
                var dto = await svc.CreateAsync(new CreateBudgetCommand(r.ScopeType, r.ScopeId, r.Name, r.MonetaryCapAmount, r.TimeCapSeconds), ct);
                return Results.Created($"/api/v1/budgets/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateBudgetRequest>>();

        group.MapPut("/{id:guid}", async (Guid id, UpdateBudgetRequest r, BudgetService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(id, new UpdateBudgetCommand(r.Name, r.MonetaryCapAmount, r.TimeCapSeconds), ct)));

        group.MapDelete("/{id:guid}", async (Guid id, BudgetService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        // Consumption + profitability for the date range -- Admin+ gated inside TimeReportService.
        group.MapGet("/{id:guid}/status", async (Guid id, DateTimeOffset from, DateTimeOffset to, TimeReportService svc, CancellationToken ct) =>
            Results.Ok(await svc.BudgetStatusAsync(id, from, to, ct)));
    }

    private static ReportGrouping ParseGrouping(string? groupBy)
        => Enum.TryParse<ReportGrouping>(groupBy, ignoreCase: true, out var g) ? g : ReportGrouping.Project;

    /// <summary>Offline-mutation-outbox replay guard (mirrors AiMobileEndpoints.IdempotencyKey): empty/whitespace reads as absent.</summary>
    private static string? IdempotencyKey(HttpContext http)
    {
        var key = http.Request.Headers["Idempotency-Key"].ToString();
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }
}
