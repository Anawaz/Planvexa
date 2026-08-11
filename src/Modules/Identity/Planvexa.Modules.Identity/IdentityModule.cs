namespace Planvexa.Modules.Identity;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Identity.Application;
using Planvexa.Modules.Identity.Application.Services;
using Planvexa.SharedContracts.Users;

/// <summary>
/// Composition marker + registration for the Identity module. The concrete <see cref="IUserStore"/>
/// implementation is provided by the Infrastructure project; entity configurations are discovered
/// by scanning this assembly.
/// </summary>
public static class IdentityModule
{
    public const string Schema = "identity";

    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<UserDataService>();
        services.AddScoped<AvatarService>();
        return services;
    }
}
