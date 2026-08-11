namespace Planvexa.Modules.Integrations.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// An issued OAuth2 access/refresh token pair, scoped and workspace-isolated exactly like
/// <see cref="PersonalAccessToken"/> — only SHA-256 hashes are stored; raw values are returned once by
/// <c>POST /oauth/token</c>. <see cref="ScopesCsv"/> is always a subset of the issuing
/// <see cref="OAuthApplication"/>'s <c>AllowedScopes</c> (narrowed at mint time by
/// <see cref="OAuthApplication.FilterScopes"/>) — the request pipeline's scope check
/// (<c>OAuthScopeEnforcementMiddleware</c>) trusts this column as the sole source of truth for what the
/// token may do.
/// </summary>
public sealed class OAuthToken : Entity, IWorkspaceOwned
{
    public const string AccessTokenPrefix = "oat_";
    public const string RefreshTokenPrefix = "oar_";

    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    private OAuthToken()
    {
    }

    private OAuthToken(
        Guid id, Guid workspaceId, Guid applicationId, Guid userId, string accessTokenHash,
        string? refreshTokenHash, string scopesCsv, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        ApplicationId = applicationId;
        UserId = userId;
        AccessTokenHash = accessTokenHash;
        RefreshTokenHash = refreshTokenHash;
        ScopesCsv = scopesCsv;
        ExpiresAtUtc = nowUtc + AccessTokenLifetime;
        RefreshExpiresAtUtc = refreshTokenHash is null ? null : nowUtc + RefreshTokenLifetime;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid UserId { get; private set; }
    public string AccessTokenHash { get; private set; } = string.Empty;
    public string? RefreshTokenHash { get; private set; }
    public string ScopesCsv { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RefreshExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public DateTimeOffset? LastUsedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyList<string> Scopes =>
        ScopesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool HasScope(string scope) => Scopes.Contains(scope, StringComparer.Ordinal);

    /// <summary>Mints a fresh access+refresh token pair. <paramref name="scopes"/> must already be
    /// narrowed to the issuing application's allowed scopes by the caller.</summary>
    public static (OAuthToken Token, string RawAccessToken, string RawRefreshToken) Create(
        Guid id, Guid workspaceId, Guid applicationId, Guid userId, IReadOnlyCollection<string> scopes, DateTimeOffset nowUtc)
    {
        var rawAccess = AccessTokenPrefix + SecretCrypto.GenerateSecret();
        var rawRefresh = RefreshTokenPrefix + SecretCrypto.GenerateSecret();
        var token = new OAuthToken(
            id, workspaceId, applicationId, userId, SecretCrypto.Hash(rawAccess),
            SecretCrypto.Hash(rawRefresh), string.Join(',', scopes), nowUtc);
        return (token, rawAccess, rawRefresh);
    }

    public bool IsAccessTokenUsable(DateTimeOffset nowUtc) => RevokedAtUtc is null && nowUtc < ExpiresAtUtc;

    public bool IsRefreshTokenUsable(DateTimeOffset nowUtc) =>
        RevokedAtUtc is null && RefreshTokenHash is not null && RefreshExpiresAtUtc is { } exp && nowUtc < exp;

    public void MarkUsed(DateTimeOffset nowUtc) => LastUsedAtUtc = nowUtc;

    public void Revoke(DateTimeOffset nowUtc) => RevokedAtUtc = nowUtc;

    /// <summary>Rotates the access token (and refresh token, one-time-use) on a refresh_token grant. The
    /// old refresh token hash is replaced so it cannot be redeemed twice.</summary>
    public (string RawAccessToken, string RawRefreshToken) Rotate(DateTimeOffset nowUtc)
    {
        var rawAccess = AccessTokenPrefix + SecretCrypto.GenerateSecret();
        var rawRefresh = RefreshTokenPrefix + SecretCrypto.GenerateSecret();
        AccessTokenHash = SecretCrypto.Hash(rawAccess);
        RefreshTokenHash = SecretCrypto.Hash(rawRefresh);
        ExpiresAtUtc = nowUtc + AccessTokenLifetime;
        RefreshExpiresAtUtc = nowUtc + RefreshTokenLifetime;
        return (rawAccess, rawRefresh);
    }
}
