namespace Planvexa.Modules.Forms;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Forms.Application.Services;

/// <summary>
/// Composition marker + DI registration for the Forms module. Store implementations and entity
/// configurations are supplied by the Infrastructure project / discovered by scanning this assembly.
/// </summary>
public static class FormsModule
{
    public const string Schema = "forms";

    public static IServiceCollection AddFormsModule(this IServiceCollection services)
    {
        services.AddScoped<FormsServiceContext>();
        services.AddScoped<FormService>();
        services.AddScoped<PublicFormService>();
        services.AddScoped<Planvexa.SharedContracts.Search.ISearchProvider, FormSearchProvider>();
        return services;
    }
}
