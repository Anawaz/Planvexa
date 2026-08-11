namespace Planvexa.Modules.Integrations.Domain;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// A workspace-owned OAuth2 client: an app that wants to act on behalf of the workspace's own users
/// (Planvexa as the OAuth PROVIDER, not a consumer — see AllowedScopes for the ceiling on what any
/// token minted for it can ever do). Follows <see cref="PersonalAccessToken"/>'s hashing/storage
/// pattern: only the client secret's SHA-256 hash is stored, the raw secret is returned once at
/// creation. A scoped access token issued to this app (<see cref="OAuthToken"/>) can never carry a
/// scope outside <see cref="AllowedScopes"/> — enforced both when the authorization code is minted
/// (<see cref="FilterScopes"/>) and, redundantly, at the request pipeline via <c>OAuthScopes</c>.
/// </summary>
public sealed class OAuthApplication : Entity, IWorkspaceOwned
{
    public const string ClientIdPrefix = "oac_";

    private OAuthApplication()
    {
    }

    private OAuthApplication(
        Guid id, Guid workspaceId, string name, string clientId, string clientSecretHash,
        string redirectUrisCsv, string allowedScopesCsv, Guid createdByUserId, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        ClientId = clientId;
        ClientSecretHash = clientSecretHash;
        RedirectUrisCsv = redirectUrisCsv;
        AllowedScopesCsv = allowedScopesCsv;
        IsActive = true;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string ClientId { get; private set; } = string.Empty;
    public string ClientSecretHash { get; private set; } = string.Empty;
    public string RedirectUrisCsv { get; private set; } = string.Empty;
    public string AllowedScopesCsv { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyList<string> RedirectUris =>
        RedirectUrisCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public IReadOnlyList<string> AllowedScopes =>
        AllowedScopesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Creates the application, returning it and the raw client secret (shown once).</summary>
    public static (OAuthApplication Application, string RawClientSecret) Create(
        Guid id, Guid workspaceId, string name, IReadOnlyCollection<string> redirectUris,
        IReadOnlyCollection<string> allowedScopes, Guid createdByUserId, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));

        var validUris = redirectUris.Select(u => u.Trim()).Where(u => u.Length > 0).ToList();
        if (validUris.Count == 0 || validUris.Any(u => !IsValidRedirectUri(u)))
        {
            throw new ValidationAppException("At least one absolute http(s) redirect URI is required.");
        }

        var validScopes = allowedScopes
            .Select(s => s.Trim())
            .Where(s => OAuthScopes.All.Contains(s))
            .Distinct()
            .ToList();
        if (validScopes.Count == 0)
        {
            throw new ValidationAppException("At least one valid scope is required.");
        }

        var clientId = ClientIdPrefix + SecretCrypto.GenerateSecret(16);
        var rawSecret = SecretCrypto.GenerateSecret();
        var app = new OAuthApplication(
            id, workspaceId, name.Trim(), clientId, SecretCrypto.Hash(rawSecret),
            string.Join(',', validUris), string.Join(',', validScopes), createdByUserId, nowUtc);
        return (app, rawSecret);
    }

    public bool VerifySecret(string rawSecret) => ClientSecretHash == SecretCrypto.Hash(rawSecret ?? string.Empty);

    public bool IsRedirectUriAllowed(string redirectUri) =>
        RedirectUris.Any(u => string.Equals(u, redirectUri, StringComparison.Ordinal));

    /// <summary>Narrows a requested scope list to the intersection with this app's own allowed scopes —
    /// the hard ceiling a token minted for this app can never exceed, regardless of what a caller asks
    /// for at the /oauth/authorize step.</summary>
    public IReadOnlyList<string> FilterScopes(IReadOnlyCollection<string> requestedScopes)
    {
        var allowed = new HashSet<string>(AllowedScopes, StringComparer.Ordinal);
        return requestedScopes.Select(s => s.Trim()).Where(allowed.Contains).Distinct().ToList();
    }

    public void Revoke() => IsActive = false;

    private static bool IsValidRedirectUri(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
}

/// <summary>The scope vocabulary an OAuth application/token can be granted. Deliberately small — extend
/// here (and nowhere else) when a new resource needs OAuth-token access.</summary>
public static class OAuthScopes
{
    public const string TasksRead = "tasks:read";
    public const string TasksWrite = "tasks:write";
    public const string WorkspaceRead = "workspace:read";
    public const string DocsRead = "docs:read";
    public const string WebhooksRead = "webhooks:read";

    public static readonly IReadOnlyList<string> All = new[] { TasksRead, TasksWrite, WorkspaceRead, DocsRead, WebhooksRead };
}
