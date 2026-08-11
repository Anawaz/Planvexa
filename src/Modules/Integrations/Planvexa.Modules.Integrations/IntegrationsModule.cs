namespace Planvexa.Modules.Integrations;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Integrations.Application.Services;

/// <summary>
/// Composition marker + DI registration for the Integrations module (webhooks + personal access
/// tokens). Store implementations and entity configurations are supplied by the Infrastructure project
/// / discovered by scanning this assembly. The host provides <c>IWebhookSender</c>.
/// </summary>
public static class IntegrationsModule
{
    public const string Schema = "integrations";

    public static IServiceCollection AddIntegrationsModule(this IServiceCollection services)
    {
        services.AddScoped<IntegrationsServiceContext>();
        services.AddScoped<WebhookService>();
        services.AddScoped<PersonalAccessTokenService>();
        services.AddScoped<WebhookDispatcher>();
        services.AddScoped<Planvexa.SharedContracts.Integrations.IWebhookDispatcher>(
            sp => sp.GetRequiredService<WebhookDispatcher>());
        services.AddScoped<Planvexa.SharedContracts.Integrations.IAccessTokenVerifier, AccessTokenVerifier>();

        // OAuth applications (Planvexa as OAuth provider) + third-party provider settings.
        services.AddScoped<OAuthApplicationService>();
        services.AddScoped<OAuthAuthorizationService>();
        services.AddScoped<Planvexa.SharedContracts.Integrations.IOAuthTokenVerifier, OAuthTokenVerifier>();
        services.AddScoped<IntegrationProviderSettingsService>();
        services.AddScoped<Planvexa.SharedContracts.Integrations.IIntegrationActionInvoker, IntegrationActionRunner>();
        return services;
    }
}
