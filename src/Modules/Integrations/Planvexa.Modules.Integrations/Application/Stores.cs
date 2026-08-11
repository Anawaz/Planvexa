namespace Planvexa.Modules.Integrations.Application;

using Planvexa.Modules.Integrations.Domain;

public interface IWebhookSubscriptionStore
{
    void Add(WebhookSubscription subscription);
    Task<WebhookSubscription?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookSubscription>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookSubscription>> ListActiveForEventAsync(Guid workspaceId, string eventType, CancellationToken ct = default);
}

public interface IWebhookDeliveryStore
{
    void Add(WebhookDelivery delivery);
    Task<bool> ExistsAsync(Guid subscriptionId, Guid eventId, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookDelivery>> ListBySubscriptionAsync(Guid subscriptionId, int max, CancellationToken ct = default);
    Task<WebhookDelivery?> FindAsync(Guid id, CancellationToken ct = default);
}

public interface IPersonalAccessTokenStore
{
    void Add(PersonalAccessToken token);
    void Remove(PersonalAccessToken token);
    Task<PersonalAccessToken?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default);
    Task<PersonalAccessToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<PersonalAccessToken>> ListForUserAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);
}

public interface IOAuthApplicationStore
{
    void Add(OAuthApplication application);
    Task<OAuthApplication?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default);
    Task<OAuthApplication?> FindByClientIdAsync(string clientId, CancellationToken ct = default);
    Task<IReadOnlyList<OAuthApplication>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface IOAuthAuthorizationCodeStore
{
    void Add(OAuthAuthorizationCode code);
    Task<OAuthAuthorizationCode?> FindByHashAsync(string codeHash, CancellationToken ct = default);
}

public interface IOAuthTokenStore
{
    void Add(OAuthToken token);
    Task<OAuthToken?> FindByAccessTokenHashAsync(string accessTokenHash, CancellationToken ct = default);
    Task<OAuthToken?> FindByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct = default);
}

public interface IIntegrationProviderSettingsStore
{
    void Add(IntegrationProviderSettings settings);
    Task<IntegrationProviderSettings?> FindAsync(Guid workspaceId, string provider, CancellationToken ct = default);
    Task<IReadOnlyList<IntegrationProviderSettings>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

/// <summary>Encrypts a provider credential at rest. Sibling of <c>Planvexa.Modules.Ai.Application.IAiSecretProtector</c>
/// (module boundaries mean Integrations cannot depend on Ai's copy) — same shape, different Data
/// Protection purpose string so the two never cross-decrypt.</summary>
public interface IIntegrationSecretProtector
{
    string Protect(string plaintext);

    /// <summary>Returns the plaintext, or empty when the value is absent or cannot be decrypted.</summary>
    string Unprotect(string protectedValue);
}
