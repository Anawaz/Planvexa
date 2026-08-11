namespace Planvexa.Modules.Whiteboards;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Whiteboards.Application.Services;

/// <summary>
/// Composition marker + DI registration for the Whiteboards module. Store
/// implementations and entity configurations are supplied by the Infrastructure project.
/// </summary>
public static class WhiteboardsModule
{
    public const string Schema = "whiteboards";

    public static IServiceCollection AddWhiteboardsModule(this IServiceCollection services)
    {
        services.AddScoped<WhiteboardServiceContext>();
        services.AddScoped<WhiteboardService>();
        services.AddScoped<WhiteboardTemplateService>();
        services.AddScoped<Planvexa.SharedContracts.Search.ISearchProvider, WhiteboardSearchProvider>();
        return services;
    }
}
