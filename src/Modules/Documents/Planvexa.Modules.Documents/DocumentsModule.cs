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
        services.AddScoped<DocumentCommentService>();
        services.AddScoped<DocumentShareLinkService>();
        services.AddScoped<DocumentSharingService>();
        services.AddScoped<Planvexa.SharedContracts.Search.ISearchProvider, DocumentSearchProvider>();

        // ADR-0003: registers Documents' single ACL resource type ("document") with Tenancy's
        // cross-module resolver — see DocumentResourceHierarchyQuery's doc comment.
        services.AddScoped<DocumentResourceHierarchyQuery>();
        services.AddScoped<Planvexa.SharedContracts.Workspaces.IResourceHierarchyQuery>(sp => sp.GetRequiredService<DocumentResourceHierarchyQuery>());
        return services;
    }
}
