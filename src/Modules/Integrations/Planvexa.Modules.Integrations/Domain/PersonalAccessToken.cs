namespace Planvexa.Modules.Integrations.Domain;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// A personal access token: a scoped, user-owned credential for API access. Only the SHA-256 hash of
/// the raw token is stored (raw shown once at creation). The owner's external subject/email/name are
/// snapshotted so authentication can reuse the standard user-provisioning path without a workspace context.
/// </summary>
public sealed class PersonalAccessToken : Entity, IWorkspaceOwned
{
    public const string Prefix = "pat_";

    private PersonalAccessToken()
    {
    }

    private PersonalAccessToken(
        Guid id, Guid workspaceId, Guid userId, string subject, string email, string displayName,
        string name, string tokenHash, string scopesCsv, DateTimeOffset? expiresAtUtc, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        Subject = subject;
        Email = email;
        DisplayName = displayName;
        Name = name;
        TokenHash = tokenHash;
        ScopesCsv = scopesCsv;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public string ScopesCsv { get; private set; } = string.Empty;
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public DateTimeOffset? LastUsedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyList<string> Scopes =>
        ScopesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Creates a token, returning the entity and the raw token value (shown once). Unrecognized
    /// scope strings are dropped rather than rejected, so a token with none of the requested scopes
    /// recognized ends up with an empty <see cref="Scopes"/> list — which <c>PatAuthenticationMiddleware</c>
    /// / <c>OAuthScopeEnforcementMiddleware</c> treat as a legacy, unrestricted (full-access) token. This
    /// keeps token creation permissive while still honoring real scopes for enforcement.</summary>
    public static (PersonalAccessToken Token, string Raw) Create(
        Guid id, Guid workspaceId, Guid userId, string subject, string email, string displayName,
        string name, IReadOnlyCollection<string> scopes, DateTimeOffset? expiresAtUtc, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstEmpty(userId, nameof(userId));

        var raw = Prefix + SecretCrypto.GenerateSecret();
        var scopesCsv = string.Join(
            ',',
            scopes.Select(s => s.Trim()).Where(s => PatScopes.All.Contains(s)).Distinct());
        var token = new PersonalAccessToken(
            id, workspaceId, userId, subject, email, displayName, name.Trim(), SecretCrypto.Hash(raw), scopesCsv, expiresAtUtc, nowUtc);
        return (token, raw);
    }

    public bool IsUsable(DateTimeOffset nowUtc) => ExpiresAtUtc is null || nowUtc < ExpiresAtUtc;

    public void MarkUsed(DateTimeOffset nowUtc) => LastUsedAtUtc = nowUtc;
}

/// <summary>The scope vocabulary a personal access token can be granted (mirrors the frontend's
/// <c>tokenScopes</c> list in IntegrationsPageClient.tsx). Broader than <c>OAuthScopes.All</c> — PATs are
/// a first-party, user-held credential rather than the OAuth privilege boundary — but the scopes that
/// overlap (tasks:read, tasks:write, docs:read, webhooks:read) use the identical string, so an endpoint
/// annotated with <c>.RequireOAuthScope(...)</c> is enforced the same way for both token types. Extend
/// here (and nowhere else) when a PAT needs access to a new resource.</summary>
public static class PatScopes
{
    public const string TasksRead = "tasks:read";
    public const string TasksWrite = "tasks:write";
    public const string DocsRead = "docs:read";
    public const string FormsWrite = "forms:write";
    public const string WebhooksRead = "webhooks:read";
    public const string ReportsRead = "reports:read";

    public static readonly IReadOnlyList<string> All =
        new[] { TasksRead, TasksWrite, DocsRead, FormsWrite, WebhooksRead, ReportsRead };
}
