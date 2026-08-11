namespace Planvexa.Modules.Integrations.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Integrations.Application;
using Planvexa.Modules.Integrations.Domain;
using Planvexa.SharedContracts.Integrations;
using Planvexa.SharedContracts.Users;

/// <summary>
/// Implements <see cref="IOAuthTokenVerifier"/> for the API host's OAuth bearer-token authentication
/// path. Mirrors <see cref="AccessTokenVerifier"/> exactly (same RLS-bootstrap reasoning in its doc
/// comment applies here): looks the token up by hash across workspaces, rejects an expired/revoked
/// token, and stamps the token's own workspace onto the accessor before the last-used write. Unlike
/// <c>PersonalAccessToken</c> (which snapshots identity at creation), an <see cref="OAuthToken"/> only
/// stores the user id — it is short-lived (1 hour) so a fresh <see cref="IUserDirectory"/> lookup (the
/// existing cross-module contract Identity already exposes, AGENTS.md rule 7) is cheap and keeps claims
/// current rather than stale.
/// </summary>
public sealed class OAuthTokenVerifier(
    IOAuthTokenStore tokens,
    IUserDirectory userDirectory,
    IClock clock,
    IWorkspaceContextAccessor workspaceAccessor,
    IUnitOfWork unitOfWork) : IOAuthTokenVerifier
{
    public async Task<OAuthTokenPrincipal?> VerifyAsync(string rawAccessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawAccessToken) || !rawAccessToken.StartsWith(OAuthToken.AccessTokenPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var hash = SecretCrypto.Hash(rawAccessToken);
        var token = await tokens.FindByAccessTokenHashAsync(hash, cancellationToken);
        if (token is null || !token.IsAccessTokenUsable(clock.UtcNow))
        {
            return null;
        }

        workspaceAccessor.Set(new WorkspaceContext(
            token.WorkspaceId, token.UserId, membershipId: null, role: string.Empty,
            permissions: new HashSet<string>(), entitlements: new HashSet<string>(), correlationId: string.Empty));

        var user = await userDirectory.FindByIdAsync(token.UserId, cancellationToken);

        token.MarkUsed(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new OAuthTokenPrincipal(
            token.UserId, token.WorkspaceId, token.ApplicationId,
            Subject: token.UserId.ToString(), Email: user?.Email ?? string.Empty, DisplayName: user?.DisplayName ?? string.Empty,
            token.Scopes);
    }
}
