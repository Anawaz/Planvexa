namespace Planvexa.SharedContracts.Integrations;

/// <summary>The resolved principal behind a valid OAuth2 access token, including the scopes it was
/// granted at authorization time (a subset of the issuing OAuth application's allowed scopes).</summary>
public sealed record OAuthTokenPrincipal(
    Guid UserId, Guid WorkspaceId, Guid ApplicationId, string Subject, string Email, string DisplayName,
    IReadOnlyList<string> Scopes);

/// <summary>
/// Contract (implemented by the Integrations module) that lets the API host authenticate a request
/// bearing an OAuth2 access token (<c>oat_...</c>), mirroring <see cref="IAccessTokenVerifier"/> for
/// personal access tokens. Verification runs with no ambient workspace; the returned principal carries
/// the owning workspace so the pipeline can bind context and the granted scopes so the pipeline can
/// enforce them. Returns null for unknown/expired/revoked tokens.
/// </summary>
public interface IOAuthTokenVerifier
{
    Task<OAuthTokenPrincipal?> VerifyAsync(string rawAccessToken, CancellationToken cancellationToken = default);
}
