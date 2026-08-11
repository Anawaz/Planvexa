namespace Planvexa.Modules.Integrations.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// A short-lived, single-use authorization code minted by <c>GET/POST /oauth/authorize</c> and redeemed
/// by <c>POST /oauth/token</c> (authorization_code grant). Only the SHA-256 hash of the raw code is
/// stored, mirroring <see cref="PersonalAccessToken"/>. Workspace-isolated: the code carries the
/// workspace of the authorizing user's session, and <see cref="OAuthAuthorizationService"/> stamps that
/// same workspace onto the minted <see cref="OAuthToken"/> — a code minted under workspace A can never
/// produce a token usable against workspace B.
/// </summary>
public sealed class OAuthAuthorizationCode : Entity, IWorkspaceOwned
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private OAuthAuthorizationCode()
    {
    }

    private OAuthAuthorizationCode(
        Guid id, Guid workspaceId, Guid applicationId, Guid userId, string codeHash,
        string redirectUri, string scopesCsv, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        ApplicationId = applicationId;
        UserId = userId;
        CodeHash = codeHash;
        RedirectUri = redirectUri;
        ScopesCsv = scopesCsv;
        ExpiresAtUtc = nowUtc + Lifetime;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid UserId { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public string RedirectUri { get; private set; } = string.Empty;
    public string ScopesCsv { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? UsedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyList<string> Scopes =>
        ScopesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static (OAuthAuthorizationCode Code, string Raw) Create(
        Guid id, Guid workspaceId, Guid applicationId, Guid userId, string redirectUri,
        IReadOnlyCollection<string> scopes, DateTimeOffset nowUtc)
    {
        var raw = SecretCrypto.GenerateSecret();
        var code = new OAuthAuthorizationCode(
            id, workspaceId, applicationId, userId, SecretCrypto.Hash(raw),
            redirectUri, string.Join(',', scopes), nowUtc);
        return (code, raw);
    }

    /// <summary>True once — redeeming a code marks it used, so a captured/replayed code can never mint a
    /// second token (a core authorization-code-flow requirement).</summary>
    public bool IsRedeemable(DateTimeOffset nowUtc) => UsedAtUtc is null && nowUtc < ExpiresAtUtc;

    public void MarkUsed(DateTimeOffset nowUtc) => UsedAtUtc = nowUtc;
}
