namespace Planvexa.Modules.Documents.Domain;

using System.Security.Cryptography;
using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// A public, read-only share link for a document — same shape and hashing/expiry/password scheme as
/// Collaboration's <c>PublicShareLink</c> (tasks), duplicated here rather than referenced because
/// Documents cannot depend on the Collaboration module (AGENTS.md rule 7; see DocumentComment's doc
/// comment for the identical precedent). Only a SHA-256 hash of the token is stored, so a DB leak does
/// not expose usable links. Always view-only — a public document link never grants comment or edit
/// access (Documents has no anonymous-comment feature, unlike tasks).
/// </summary>
public sealed class DocumentShareLink : Entity, IAggregateRoot, IWorkspaceOwned
{
    private DocumentShareLink()
    {
    }

    private DocumentShareLink(
        Guid id, Guid workspaceId, Guid documentId, string tokenHash,
        Guid createdByUserId, DateTimeOffset createdAtUtc, DateTimeOffset? expiresAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        DocumentId = documentId;
        TokenHash = tokenHash;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    private const int Pbkdf2Iterations = 100_000;
    private const int Pbkdf2SaltSize = 16;
    private const int Pbkdf2HashSize = 32;

    public Guid WorkspaceId { get; private set; }
    public Guid DocumentId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public bool IsRevoked { get; private set; }

    /// <summary>
    /// PBKDF2 hash of an optional access password, stored as "{iterations}.{saltBase64}.{hashBase64}".
    /// Null means the link needs no password. The raw password is never stored.
    /// </summary>
    public string? PasswordHash { get; private set; }

    public bool RequiresPassword => PasswordHash is not null;

    public static (DocumentShareLink Link, string RawToken) Create(
        Guid id, Guid workspaceId, Guid documentId, Guid createdByUserId, DateTimeOffset nowUtc, TimeSpan? validFor)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(documentId, nameof(documentId));

        var rawToken = GenerateToken();
        var expires = validFor.HasValue ? nowUtc.Add(validFor.Value) : (DateTimeOffset?)null;
        var link = new DocumentShareLink(id, workspaceId, documentId, HashToken(rawToken), createdByUserId, nowUtc, expires);
        return (link, rawToken);
    }

    public bool IsUsable(DateTimeOffset nowUtc)
        => !IsRevoked && (ExpiresAtUtc is null || nowUtc < ExpiresAtUtc);

    public void Revoke() => IsRevoked = true;

    /// <summary>Sets or clears (pass null/empty) the access password. Never stores the raw value.</summary>
    public void SetPassword(string? rawPassword)
        => PasswordHash = string.IsNullOrEmpty(rawPassword) ? null : HashPassword(rawPassword);

    /// <summary>True when no password is set, or the candidate matches. Constant-time comparison.</summary>
    public bool VerifyPassword(string? candidate)
    {
        if (PasswordHash is null)
        {
            return true;
        }

        if (candidate is null)
        {
            return false;
        }

        var parts = PasswordHash.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            System.Text.Encoding.UTF8.GetBytes(candidate), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static string HashPassword(string rawPassword)
    {
        var salt = RandomNumberGenerator.GetBytes(Pbkdf2SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            System.Text.Encoding.UTF8.GetBytes(rawPassword), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, Pbkdf2HashSize);
        return $"{Pbkdf2Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexStringLower(bytes);
    }

    private static string GenerateToken()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToHexStringLower(buffer);
    }
}
