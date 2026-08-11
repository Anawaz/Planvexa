namespace Planvexa.Modules.Integrations.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Integrations.Application;
using Planvexa.Modules.Integrations.Domain;
using Planvexa.SharedContracts.Integrations;

/// <summary>
/// Implements <see cref="IAccessTokenVerifier"/> for the API host's PAT authentication path. Looks the
/// token up by hash (across workspaces — the token itself proves the workspace), rejects expired
/// tokens, and stamps last-used. Verification happens before <c>WorkspaceResolutionMiddleware</c> binds
/// the ambient Workspace context for the rest of the request, but the last-used UPDATE below still
/// needs to satisfy the hardened workspace_isolation RLS policy (0029: ambient workspace required, no
/// escape hatch) — so this stamps the token's own workspace onto the accessor first, matching the
/// pattern <c>WorkspaceRegistrationService</c> uses for its own bootstrap write.
/// </summary>
public sealed class AccessTokenVerifier(
    IPersonalAccessTokenStore tokens,
    IClock clock,
    IWorkspaceContextAccessor workspaceAccessor,
    IUnitOfWork unitOfWork) : IAccessTokenVerifier
{
    public async Task<AccessTokenPrincipal?> VerifyAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || !rawToken.StartsWith(PersonalAccessToken.Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var hash = SecretCrypto.Hash(rawToken);
        var token = await tokens.FindByHashAsync(hash, cancellationToken);
        if (token is null || !token.IsUsable(clock.UtcNow))
        {
            return null;
        }

        workspaceAccessor.Set(new WorkspaceContext(
            token.WorkspaceId, token.UserId, membershipId: null, role: string.Empty,
            permissions: new HashSet<string>(), entitlements: new HashSet<string>(), correlationId: string.Empty));

        token.MarkUsed(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccessTokenPrincipal(
            token.UserId, token.WorkspaceId, token.Subject, token.Email, token.DisplayName, token.Scopes);
    }
}
