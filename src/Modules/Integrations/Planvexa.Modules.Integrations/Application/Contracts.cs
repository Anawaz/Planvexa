namespace Planvexa.Modules.Integrations.Application;

// ---- DTOs ----
public sealed record WebhookDto(Guid Id, string Url, IReadOnlyList<string> EventTypes, bool IsActive, DateTimeOffset CreatedAtUtc);

/// <summary>Returned once on creation — includes the signing secret (never shown again).</summary>
public sealed record CreatedWebhookDto(Guid Id, string Url, IReadOnlyList<string> EventTypes, bool IsActive, DateTimeOffset CreatedAtUtc, string Secret);

public sealed record WebhookDeliveryDto(Guid Id, string EventType, int Attempt, bool Success, int? StatusCode, string? Detail, DateTimeOffset OccurredAtUtc);

public sealed record TokenDto(Guid Id, string Name, IReadOnlyList<string> Scopes, DateTimeOffset? LastUsedAtUtc, DateTimeOffset? ExpiresAtUtc, DateTimeOffset CreatedAtUtc);

/// <summary>Returned once on creation — includes the raw token (never shown again).</summary>
public sealed record CreatedTokenDto(Guid Id, string Name, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAtUtc, DateTimeOffset CreatedAtUtc, string Token);

// ---- Commands ----
public sealed record CreateWebhookCommand(string Url, IReadOnlyList<string> EventTypes);

public sealed record CreateTokenCommand(string Name, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAtUtc);

// ---- OAuth applications ----
public sealed record OAuthApplicationDto(
    Guid Id, string Name, string ClientId, IReadOnlyList<string> RedirectUris, IReadOnlyList<string> AllowedScopes,
    bool IsActive, DateTimeOffset CreatedAtUtc);

/// <summary>Returned once on creation — includes the raw client secret (never shown again).</summary>
public sealed record CreatedOAuthApplicationDto(
    Guid Id, string Name, string ClientId, string ClientSecret, IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> AllowedScopes, DateTimeOffset CreatedAtUtc);

public sealed record CreateOAuthApplicationCommand(string Name, IReadOnlyList<string> RedirectUris, IReadOnlyList<string> AllowedScopes);

public sealed record OAuthAuthorizeCommand(string ClientId, string RedirectUri, IReadOnlyList<string> Scopes);

/// <summary>The one-time authorization code plus the redirect the caller should send the browser to.</summary>
public sealed record OAuthAuthorizeResultDto(string Code, string RedirectUri);

public sealed record OAuthTokenResultDto(
    string AccessToken, string TokenType, int ExpiresInSeconds, string? RefreshToken, string Scope);

// ---- Integration provider settings ----
public sealed record IntegrationProviderSettingsDto(string Provider, string ConfigJson, string SecretHint, bool IsEnabled, bool HasRealImplementation);

public sealed record UpdateIntegrationProviderSettingsCommand(string ConfigJson, string? Secret, bool IsEnabled);
