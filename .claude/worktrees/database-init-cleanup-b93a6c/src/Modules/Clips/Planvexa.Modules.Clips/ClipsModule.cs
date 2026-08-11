namespace Planvexa.Modules.Clips;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Clips.Application.Services;

/// <summary>
/// Composition marker + DI registration for the Clips module. Store implementations
/// and entity configurations are supplied by the Infrastructure project.
/// </summary>
public static class ClipsModule
{
    public const string Schema = "clips";

    public static IServiceCollection AddClipsModule(this IServiceCollection services)
    {
        services.AddScoped<ClipServiceContext>();
        services.AddScoped<ClipService>();
        services.AddScoped<ClipCommentService>();
        services.AddScoped<ClipTranscriptService>();
        services.AddScoped<Planvexa.SharedContracts.Search.ISearchProvider, ClipSearchProvider>();
        return services;
    }
}
