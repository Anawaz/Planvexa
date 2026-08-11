namespace Planvexa.Modules.Ai;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Ai.Application.Services;

/// <summary>
/// Composition marker + DI registration for the AI module. The completion provider is supplied by the
/// host (a deterministic default, or a real LLM); store implementations + the task content source are
/// supplied by Infrastructure; entity configurations are discovered by scanning this assembly.
/// </summary>
public static class AiModule
{
    public const string Schema = "ai";

    public static IServiceCollection AddAiModule(this IServiceCollection services)
    {
        services.AddScoped<AiServiceContext>();
        services.AddScoped<AiAssistService>();
        services.AddScoped<AiSettingsService>();
        return services;
    }
}
