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

    /// <summary>Creates a token, returning the entity and the raw token value (shown once).</summary>
    public static (PersonalAccessToken Token, string Raw) Create(
        Guid id, Guid workspaceId, Guid userId, string subject, string email, string displayName,
        string name, IReadOnlyCollection<string> scopes, DateTimeOffset? expiresAtUtc, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstEmpty(userId, nameof(userId));

        var raw = Prefix + SecretCrypto.GenerateSecret();
        var scopesCsv = string.Join(',', scopes.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct());
        var token = new PersonalAccessToken(
            id, workspaceId, userId, subject, email, displayName, name.Trim(), SecretCrypto.Hash(raw), scopesCsv, expiresAtUtc, nowUtc);
        return (token, raw);
    }

    public bool IsUsable(DateTimeOffset nowUtc) => ExpiresAtUtc is null || nowUtc < ExpiresAtUtc;

    public void MarkUsed(DateTimeOffset nowUtc) => LastUsedAtUtc = nowUtc;
}
