namespace Planvexa.Modules.Mobile;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Mobile.Application.Services;

/// <summary>
/// Composition marker + DI registration for the Mobile module. Store implementations and entity
/// configurations are supplied by the Infrastructure project / discovered by scanning this assembly.
/// </summary>
public static class MobileModule
{
    public const string Schema = "mobile";

    public static IServiceCollection AddMobileModule(this IServiceCollection services)
    {
        services.AddScoped<MobileServiceContext>();
        services.AddScoped<DeviceService>();
        services.AddScoped<SyncService>();
        return services;
    }
}
