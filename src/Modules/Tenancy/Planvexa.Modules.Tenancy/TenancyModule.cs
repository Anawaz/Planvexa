namespace Planvexa.Modules.Tenancy;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Tenancy.Application;
using Planvexa.Modules.Tenancy.Authorization;

/// <summary>
/// Composition marker + DI registration for the Tenancy module. The <see cref="IWorkspaceResolver"/>
/// and <see cref="IRoleStore"/> store implementations are provided by Infrastructure.
/// </summary>
public static class TenancyModule
{
    public const string Schema = "tenancy";

    public static IServiceCollection AddTenancyModule(this IServiceCollection services)
    {
        services.AddScoped<WorkspaceRegistrationService>();
        services.AddScoped<WorkspaceService>();
        services.AddScoped<MembershipService>();
        services.AddScoped<InvitationService>();
        services.AddScoped<Planvexa.SharedContracts.Tenancy.IInvitationDirectoryQuery>(sp => sp.GetRequiredService<InvitationService>());
        services.AddScoped<TeamService>();
        services.AddScoped<Planvexa.SharedContracts.Search.ISearchProvider, TenancySearchProvider>();
        services.AddScoped<Planvexa.SharedContracts.Teams.ITeamDirectoryQuery, TeamDirectoryQuery>();
        services.AddScoped<FeatureService>();
        services.AddScoped<RoleService>();
        services.AddScoped<IRolePermissionResolver, RolePermissionResolver>();
        services.AddScoped<Planvexa.SharedContracts.Workspaces.IWorkspaceAccessQuery, WorkspaceAccessQuery>();
        services.AddScoped<Planvexa.SharedContracts.Workspaces.IWorkspaceRosterQuery, WorkspaceRosterQuery>();

        // ADR-0003: per-resource ACL. Registered under both cross-module contracts since one
        // service implements both (see ResourcePermissionService doc comment).
        services.AddScoped<ResourcePermissionService>();
        services.AddScoped<Planvexa.SharedContracts.Workspaces.IResourcePermissionQuery>(sp => sp.GetRequiredService<ResourcePermissionService>());
        services.AddScoped<Planvexa.SharedContracts.Workspaces.IResourcePermissionAdmin>(sp => sp.GetRequiredService<ResourcePermissionService>());
        return services;
    }
}
