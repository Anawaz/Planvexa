namespace Planvexa.Modules.Reporting;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Reporting.Application.Services;

/// <summary>
/// Composition marker + DI registration for the Reporting module. Store implementations and entity
/// configurations are supplied by the Infrastructure project / discovered by scanning this assembly.
/// </summary>
public static class ReportingModule
{
    public const string Schema = "reporting";

    public static IServiceCollection AddReportingModule(this IServiceCollection services)
    {
        services.AddScoped<ReportingServiceContext>();
        services.AddScoped<WidgetComputer>();
        services.AddScoped<DashboardService>();
        services.AddScoped<PortfolioService>();
        services.AddScoped<Planvexa.SharedContracts.Search.ISearchProvider, DashboardSearchProvider>();

        // Goals/OKRs & reporting completeness.
        services.AddScoped<RiskService>();
        services.AddScoped<DrillDownService>();
        services.AddScoped<ScheduledReportService>();
        services.AddScoped<ScheduledReportRunner>();
        services.AddScoped<PdfExportService>();
        return services;
    }
}
