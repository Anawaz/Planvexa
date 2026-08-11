namespace Planvexa.Modules.Automations;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Automations.Application.Services;

/// <summary>
/// Composition marker + DI registration for the Automations module. Store implementations and entity
/// configurations are supplied by the Infrastructure project / discovered by scanning this assembly.
/// </summary>
public static class AutomationsModule
{
    public const string Schema = "automation";

    public static IServiceCollection AddAutomationsModule(this IServiceCollection services)
    {
        services.AddScoped<AutomationsServiceContext>();
        services.AddScoped<AutomationRuleService>();
        services.AddScoped<AutomationDispatcher>();
        services.AddScoped<Planvexa.SharedContracts.Automations.IAutomationDispatcher>(
            sp => sp.GetRequiredService<AutomationDispatcher>());

        // Background-sweep runners (due-date/scheduled/SLA triggers + retry) — see
        // SweepRunners.cs's class doc. The composition root's *BackgroundService classes resolve these.
        services.AddScoped<DueDateSweepRunner>();
        services.AddScoped<ScheduledAutomationSweepRunner>();
        services.AddScoped<SlaSweepRunner>();
        services.AddScoped<AutomationRetryRunner>();
        return services;
    }
}
