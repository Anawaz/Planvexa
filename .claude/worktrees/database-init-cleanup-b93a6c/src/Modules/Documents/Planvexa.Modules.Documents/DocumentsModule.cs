namespace Planvexa.Modules.Documents;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Documents.Application.Services;

/// <summary>
/// Composition marker + DI registration for the Documents module. Store implementations and entity
/// configurations are supplied by the Infrastructure project / discovered by scanning this assembly.
/// </summary>
public static class DocumentsModule
{
    public const string Schema = "docs";

    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        services.AddScoped<DocumentsServiceContext>();
        services.AddScoped<DocumentService>();
        services.AddScoped<DocumentTemplateService>();
        services.AddScoped<Planvexa.SharedContracts.Search.ISearchProvider, DocumentSearchProvider>();
        return services;
    }
}
