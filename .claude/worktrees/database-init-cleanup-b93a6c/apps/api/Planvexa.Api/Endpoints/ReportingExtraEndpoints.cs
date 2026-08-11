namespace Planvexa.Api.Endpoints;

using FluentValidation;
using Planvexa.Modules.Reporting.Application;
using Planvexa.Modules.Reporting.Application.Services;
using Planvexa.Modules.Reporting.Domain;

// ---- Request models ----
public sealed record CreateRiskRequest(string Title, string? Description, RiskSeverity Severity, RiskScopeType ScopeType, Guid ScopeId);

public sealed record UpdateRiskRequest(string? Title, string? Description, RiskSeverity? Severity, RiskStatus? Status);

public sealed record CreateScheduledReportRequest(Guid DashboardId, IReadOnlyList<string> Recipients, ScheduledReportCadence Cadence);

public sealed record SetScheduledReportEnabledRequest(bool Enabled);

public sealed class CreateRiskRequestValidator : AbstractValidator<CreateRiskRequest>
{
    public CreateRiskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ScopeId).NotEmpty();
    }
}

public sealed class CreateScheduledReportRequestValidator : AbstractValidator<CreateScheduledReportRequest>
{
    public CreateScheduledReportRequestValidator()
    {
        RuleFor(x => x.DashboardId).NotEmpty();
        RuleFor(x => x.Recipients).NotEmpty();
    }
}

/// <summary>reporting-completeness endpoints: risk register, permission-filtered drill-down,
/// scheduled reports, PDF export.</summary>
public static class ReportingExtraEndpoints
{
    public static void MapReportingExtraEndpoints(this RouteGroupBuilder api)
    {
        MapRisks(api);
        MapDrillDown(api);
        MapScheduledReports(api);
        MapPdfExport(api);
    }

    private static void MapRisks(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/reporting/risks").RequireAuthorization();

        group.MapGet("/", async (RiskService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateRiskRequest r, RiskService svc, CancellationToken ct) =>
            {
                var dto = await svc.CreateAsync(new CreateRiskCommand(r.Title, r.Description, r.Severity, r.ScopeType, r.ScopeId), ct);
                return Results.Created($"/api/v1/reporting/risks/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateRiskRequest>>();

        group.MapPut("/{id:guid}", async (Guid id, UpdateRiskRequest r, RiskService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(id, new UpdateRiskCommand(r.Title, r.Description, r.Severity, r.Status), ct)));

        group.MapDelete("/{id:guid}", async (Guid id, RiskService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });
    }

    private static void MapDrillDown(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/reporting/drill-down").RequireAuthorization();

        group.MapGet("/overdue", async (DrillDownService svc, CancellationToken ct) =>
            Results.Ok(await svc.OverdueAsync(ct)));

        group.MapGet("/completed", async (DateTimeOffset fromUtc, DateTimeOffset toUtc, DrillDownService svc, CancellationToken ct) =>
            Results.Ok(await svc.CompletedAsync(fromUtc, toUtc, ct)));

        group.MapGet("/spaces/{spaceId:guid}", async (Guid spaceId, bool? completedOnly, DrillDownService svc, CancellationToken ct) =>
            Results.Ok(await svc.SpaceAsync(spaceId, completedOnly, ct)));
    }

    private static void MapScheduledReports(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/reporting/scheduled-reports").RequireAuthorization();

        group.MapGet("/", async (ScheduledReportService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateScheduledReportRequest r, ScheduledReportService svc, CancellationToken ct) =>
            {
                var dto = await svc.CreateAsync(new CreateScheduledReportCommand(r.DashboardId, r.Recipients, r.Cadence), ct);
                return Results.Created($"/api/v1/reporting/scheduled-reports/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateScheduledReportRequest>>();

        group.MapPut("/{id:guid}/enabled", async (Guid id, SetScheduledReportEnabledRequest r, ScheduledReportService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetEnabledAsync(id, r.Enabled, ct)));

        group.MapDelete("/{id:guid}", async (Guid id, ScheduledReportService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });
    }

    private static void MapPdfExport(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/reporting/portfolio").RequireAuthorization();

        group.MapGet("/export.pdf", async (DateTimeOffset? fromUtc, DateTimeOffset? toUtc, PdfExportService svc, CancellationToken ct) =>
        {
            var pdf = await svc.PortfolioPdfAsync(fromUtc, toUtc, ct);
            return Results.File(pdf, "application/pdf", "portfolio-summary.pdf");
        });
    }
}
