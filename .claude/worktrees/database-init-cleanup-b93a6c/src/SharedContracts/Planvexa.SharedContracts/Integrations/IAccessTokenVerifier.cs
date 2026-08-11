namespace Planvexa.SharedContracts.Integrations;

/// <summary>The resolved principal behind a valid personal access token.</summary>
public sealed record AccessTokenPrincipal(
    Guid UserId, Guid WorkspaceId, string Subject, string Email, string DisplayName, IReadOnlyList<string> Scopes);

/// <summary>
/// Contract (implemented by the Integrations module) that lets the API host authenticate a request
/// bearing a personal access token. Verification runs with no ambient workspace (the token lookup is
/// by hash across all workspaces); the returned principal carries the owning workspace so the pipeline
/// can bind context. Returns null for unknown/expired tokens.
/// </summary>
public interface IAccessTokenVerifier
{
    Task<AccessTokenPrincipal?> VerifyAsync(string rawToken, CancellationToken cancellationToken = default);
}
