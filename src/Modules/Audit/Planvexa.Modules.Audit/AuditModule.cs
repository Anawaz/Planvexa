namespace Planvexa.Modules.Audit;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.Modules.Audit.Application;

public static class AuditModule
{
    public const string Schema = "audit";

    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        services.AddScoped<IAuditWriter, AuditWriter>();
        return services;
    }
}
