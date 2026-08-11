namespace Planvexa.Modules.Governance;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Governance.Application.Services;

/// <summary>
/// Composition marker + DI registration for the Governance module. Store implementations and entity
/// configurations are supplied by the Infrastructure project / discovered by scanning this assembly.
/// </summary>
public static class GovernanceModule
{
    public const string Schema = "governance";

    public static IServiceCollection AddGovernanceModule(this IServiceCollection services)
    {
        services.AddScoped<GovernanceServiceContext>();
        services.AddScoped<AuditLogService>();
        services.AddScoped<SecuritySettingsService>();
        services.AddScoped<ExportJobService>();
        services.AddScoped<ExportRunner>();
        services.AddScoped<IpAllowListService>();
        services.AddScoped<RetentionService>();
        services.AddScoped<RetentionRunner>();
        return services;
    }
}

