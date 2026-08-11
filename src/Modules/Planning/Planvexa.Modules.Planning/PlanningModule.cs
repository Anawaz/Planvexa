namespace Planvexa.Modules.Planning;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Planning.Application.Services;

/// <summary>
/// Composition marker + DI registration for the Planning module. Store implementations and entity
/// configurations are supplied by the Infrastructure project / discovered by scanning this assembly.
/// </summary>
public static class PlanningModule
{
    public const string Schema = "planning";

    public static IServiceCollection AddPlanningModule(this IServiceCollection services)
    {
        services.AddScoped<PlanningServiceContext>();
        services.AddScoped<PlanningCalendarService>();
        services.AddScoped<EstimateService>();
        services.AddScoped<SprintService>();
        services.AddScoped<WorkloadService>();
        services.AddScoped<TeamWorkloadService>();
        services.AddScoped<Planvexa.SharedContracts.Reporting.IPlanningQueries, PlanningQueries>();
        return services;
    }
}
