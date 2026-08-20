namespace Planvexa.Api.Platform;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Planvexa.SharedContracts.Platform;

/// <summary>
/// Reads and writes the Keycloak realm's <c>registrationAllowed</c> flag through the Admin REST API, so
/// the host console's self-registration toggle controls account creation for real rather than only
/// Planvexa's half of it (see <see cref="IIdentityProviderRegistration"/>).
///
/// Credentials are OPTIONAL and this is only registered when they are present — realm-admin rights are
/// a serious thing to hand an application, and an operator who would rather manage Keycloak themselves
/// keeps the no-op implementation and an honest warning in the console. Two shapes are supported:
///
/// <list type="bullet">
/// <item><c>Keycloak:AdminClientId</c> + <c>Keycloak:AdminClientSecret</c> — a confidential client with
/// a service account holding the <c>manage-realm</c> role. Preferred: scoped to one realm, revocable,
/// and no human password in configuration.</item>
/// <item><c>Keycloak:AdminUser</c> + <c>Keycloak:AdminPassword</c> — the master admin via
/// <c>admin-cli</c>. Simpler, and what a local dev stack already has; far broader privilege.</item>
/// </list>
///
/// Both the admin base URL and the realm are derived from <c>Keycloak:Authority</c> (which is already
/// configured for token validation) so a deployment behind a path prefix — this instance serves
/// Keycloak at <c>/idp</c> — needs no additional URL settings.
/// </summary>
public sealed class KeycloakRegistrationAdmin(
    IHttpClientFactory httpClientFactory,
    KeycloakAdminOptions options,
    ILogger<KeycloakRegistrationAdmin> logger) : IIdentityProviderRegistration
{
    public const string ClientName = "keycloak-admin";

    public async Task<IdentityProviderRegistrationState> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = await AuthenticatedClientAsync(cancellationToken);
            using var response = await client.GetAsync(new Uri($"admin/realms/{options.Realm}", UriKind.Relative), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Unreachable($"Keycloak returned {(int)response.StatusCode} reading realm '{options.Realm}'.");
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var allowed = document.RootElement.TryGetProperty("registrationAllowed", out var value) && value.GetBoolean();
            return new IdentityProviderRegistrationState(Manageable: true, RegistrationAllowed: allowed, Detail: null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the Keycloak realm's registration setting.");
            return Unreachable(ex.Message);
        }
    }

    public async Task<IdentityProviderRegistrationState> SetAsync(bool allowed, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = await AuthenticatedClientAsync(cancellationToken);

            // A partial representation is enough — Keycloak merges it into the existing realm rather
            // than replacing it, so this cannot clobber unrelated realm configuration.
            using var response = await client.PutAsJsonAsync(
                new Uri($"admin/realms/{options.Realm}", UriKind.Relative),
                new { realm = options.Realm, registrationAllowed = allowed },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var detail = $"Keycloak returned {(int)response.StatusCode} updating realm '{options.Realm}'.";
                logger.LogWarning("{Detail}", detail);
                return Unreachable(detail);
            }

            logger.LogInformation(
                "Set Keycloak realm '{Realm}' registrationAllowed to {Allowed}.", options.Realm, allowed);
            return new IdentityProviderRegistrationState(Manageable: true, RegistrationAllowed: allowed, Detail: null);
        }
        catch (Exception ex)
        {
            // Deliberately swallowed: the Planvexa-side setting is already committed by the time this
            // runs, and throwing here would fail a save that actually succeeded.
            logger.LogWarning(ex, "Could not update the Keycloak realm's registration setting.");
            return Unreachable(ex.Message);
        }
    }

    private static IdentityProviderRegistrationState Unreachable(string detail)
        => new(Manageable: true, RegistrationAllowed: null, Detail: detail);

    private async Task<HttpClient> AuthenticatedClientAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(ClientName);
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(client, cancellationToken));
        return client;
    }

    /// <summary>
    /// Fetched per operation rather than cached: these calls happen only when a host administrator
    /// saves settings or opens the settings page, so a token cache would add invalidation bugs to save
    /// nothing measurable.
    /// </summary>
    private async Task<string> AccessTokenAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var form = options.UseServiceAccount
            ? new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = options.AdminClientId!,
                ["client_secret"] = options.AdminClientSecret!,
            }
            : new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = options.AdminUser!,
                ["password"] = options.AdminPassword!,
            };

        // A service-account client lives in the realm it administers; the master admin lives in `master`.
        var tokenRealm = options.UseServiceAccount ? options.Realm : "master";
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri($"realms/{tokenRealm}/protocol/openid-connect/token", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(form),
        };

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Keycloak returned no access_token.");
    }
}

/// <summary>
/// Resolved Keycloak admin configuration. <see cref="TryCreate"/> returns null when no credentials are
/// supplied, which is what decides whether the real implementation or the no-op one is registered.
/// </summary>
public sealed class KeycloakAdminOptions
{
    public required string BaseUrl { get; init; }
    public required string Realm { get; init; }
    public string? AdminClientId { get; init; }
    public string? AdminClientSecret { get; init; }
    public string? AdminUser { get; init; }
    public string? AdminPassword { get; init; }

    public bool UseServiceAccount => !string.IsNullOrWhiteSpace(AdminClientId) && !string.IsNullOrWhiteSpace(AdminClientSecret);

    public static KeycloakAdminOptions? TryCreate(IConfiguration configuration)
    {
        var authority = configuration["Keycloak:Authority"];
        if (string.IsNullOrWhiteSpace(authority))
        {
            return null;
        }

        // Authority is ".../realms/{realm}" — split it rather than asking the operator to configure the
        // base URL and realm a second time, which is one more thing to get out of step.
        var marker = authority.IndexOf("/realms/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var baseUrl = authority[..marker];
        var realm = authority[(marker + "/realms/".Length)..].Trim('/');
        if (string.IsNullOrWhiteSpace(realm))
        {
            return null;
        }

        var options = new KeycloakAdminOptions
        {
            BaseUrl = baseUrl,
            Realm = realm,
            AdminClientId = configuration["Keycloak:AdminClientId"],
            AdminClientSecret = configuration["Keycloak:AdminClientSecret"],
            AdminUser = configuration["Keycloak:AdminUser"],
            AdminPassword = configuration["Keycloak:AdminPassword"],
        };

        var hasCredentials = options.UseServiceAccount
            || (!string.IsNullOrWhiteSpace(options.AdminUser) && !string.IsNullOrWhiteSpace(options.AdminPassword));

        return hasCredentials ? options : null;
    }
}
