namespace Planvexa.Modules.Integrations.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Integrations.Application;
using Planvexa.Modules.Integrations.Authorization;
using Planvexa.Modules.Integrations.Domain;

/// <summary>
/// Manages the acting user's personal access tokens. Any workspace member may manage their own tokens;
/// a token is scoped to the current workspace and its owner. The raw token is returned only once.
/// </summary>
public sealed class PersonalAccessTokenService(
    IntegrationsServiceContext ctx,
    IPersonalAccessTokenStore tokens)
    : IntegrationsServiceBase(ctx)
{
    public async Task<IReadOnlyList<TokenDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        IntegrationsAuthorizer.EnsureMember((await AccessAsync(workspaceId, ct))?.Role);

        var list = await tokens.ListForUserAsync(workspaceId, UserId, ct);
        return list
            .Select(t => new TokenDto(t.Id, t.Name, t.Scopes, t.LastUsedAtUtc, t.ExpiresAtUtc, t.CreatedAtUtc))
            .ToList();
    }

    public async Task<CreatedTokenDto> CreateAsync(CreateTokenCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        IntegrationsAuthorizer.EnsureMember((await AccessAsync(workspaceId, ct))?.Role);

        var user = Ctx.CurrentUser;
        var (token, raw) = PersonalAccessToken.Create(
            NewId(), workspaceId, UserId, user.Subject, user.Email, user.DisplayName,
            command.Name, command.Scopes, command.ExpiresAtUtc, Now);
        tokens.Add(token);
        Audit("integrations.pat.created", "PersonalAccessToken", token.Id, new { token.Name, token.ScopesCsv });
        await SaveAsync(ct);

        return new CreatedTokenDto(token.Id, token.Name, token.Scopes, token.ExpiresAtUtc, token.CreatedAtUtc, raw);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        IntegrationsAuthorizer.EnsureMember((await AccessAsync(workspaceId, ct))?.Role);

        var token = await tokens.FindAsync(workspaceId, id, ct)
            ?? throw new NotFoundException("Token not found.");
        if (token.UserId != UserId)
        {
            // A user may only revoke their own tokens.
            throw new ForbiddenException("You can only revoke your own tokens.");
        }

        tokens.Remove(token);
        Audit("integrations.pat.revoked", "PersonalAccessToken", id);
        await SaveAsync(ct);
    }
}
