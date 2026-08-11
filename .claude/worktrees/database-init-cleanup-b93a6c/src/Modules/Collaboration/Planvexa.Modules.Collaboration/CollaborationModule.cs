namespace Planvexa.Modules.Collaboration;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Collaboration.Application;

public static class CollaborationModule
{
    public const string Schema = "collab";

    public static IServiceCollection AddCollaborationModule(this IServiceCollection services)
    {
        services.AddScoped<CommentService>();
        services.AddScoped<ShareLinkService>();
        services.AddScoped<Planvexa.SharedContracts.Search.ISearchProvider, CommentSearchProvider>();
        services.AddScoped<Planvexa.SharedContracts.Collaboration.ICommentWriteApi, CommentWriteApi>();
        return services;
    }
}
