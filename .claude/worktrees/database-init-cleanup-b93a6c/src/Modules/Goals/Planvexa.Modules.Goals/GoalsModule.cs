namespace Planvexa.Modules.Goals;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Goals.Application.Services;

public static class GoalsModule
{
    public const string Schema = "goals";

    public static IServiceCollection AddGoalsModule(this IServiceCollection services)
    {
        services.AddScoped<GoalServiceContext>();
        services.AddScoped<GoalService>();
        services.AddScoped<GoalFolderService>();
        services.AddScoped<GoalCommentService>();
        return services;
    }
}
