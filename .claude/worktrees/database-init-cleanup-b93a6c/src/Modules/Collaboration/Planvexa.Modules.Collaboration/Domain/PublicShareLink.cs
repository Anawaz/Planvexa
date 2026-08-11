namespace Planvexa.Modules.Collaboration.Domain;

using System.Security.Cryptography;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// A public, read-only share link for a task. Only a SHA-256 hash of the token is stored, so a DB
/// leak does not expose usable links. The anonymous read path returns ONLY the shared task's
/// projection — never siblings, comments, or other workspace data.
/// </summary>
public sealed class PublicShareLink : Entity, IAggregateRoot, IWorkspaceOwned
{
    private PublicShareLink()
    {
    }

    private PublicShareLink(
        Guid id, Guid workspaceId, Guid taskId, string tokenHash,
        Guid createdByUserId, DateTimeOffset createdAtUtc, DateTimeOffset? expiresAtUtc, PermissionLevel level)
        : base(id)
    {
        WorkspaceId = workspaceId;
        TaskId = taskId;
        TokenHash = tokenHash;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Level = level;
    }

    /// <summary>Permission levels a public link may grant. Anything above Comment (Edit and up) stays internal-only.</summary>
    public static readonly IReadOnlySet<PermissionLevel> AllowedLevels = new HashSet<PermissionLevel> { PermissionLevel.View, PermissionLevel.Comment };

    private const int Pbkdf2Iterations = 100_000;
    private const int Pbkdf2SaltSize = 16;
    private const int Pbkdf2HashSize = 32;

    public Guid WorkspaceId { get; private set; }
    public Guid TaskId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public bool IsRevoked { get; private set; }

    /// <summary>View-only (default) or View+Comment. Never Edit or above — a public link cannot mutate the task.</summary>
    public PermissionLevel Level { get; private set; } = PermissionLevel.View;

    public bool AllowsComments => Level >= PermissionLevel.Comment;

    /// <summary>
    /// PBKDF2 hash of an optional access password, stored as "{iterations}.{saltBase64}.{hashBase64}".
    /// Null means the link needs no password (today's behavior). The raw password is never stored.
    /// </summary>
    public string? PasswordHash { get; private set; }

    public bool RequiresPassword => PasswordHash is not null;

    public static (PublicShareLink Link, string RawToken) Create(
        Guid id, Guid workspaceId, Guid taskId, Guid createdByUserId, DateTimeOffset nowUtc, TimeSpan? validFor,
        PermissionLevel level = PermissionLevel.View)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(taskId, nameof(taskId));
        if (!AllowedLevels.Contains(level))
        {
            throw new BuildingBlocks.Exceptions.ValidationAppException("A public link may only grant View or Comment access.");
        }

        var rawToken = GenerateToken();
        var expires = validFor.HasValue ? nowUtc.Add(validFor.Value) : (DateTimeOffset?)null;
        var link = new PublicShareLink(id, workspaceId, taskId, HashToken(rawToken), createdByUserId, nowUtc, expires, level);
        return (link, rawToken);
    }

    public bool IsUsable(DateTimeOffset nowUtc)
        => !IsRevoked && (ExpiresAtUtc is null || nowUtc < ExpiresAtUtc);

    public void Revoke() => IsRevoked = true;

    public void SetPermissionLevel(PermissionLevel level)
    {
        if (!AllowedLevels.Contains(level))
        {
            throw new BuildingBlocks.Exceptions.ValidationAppException("A public link may only grant View or Comment access.");
        }

        Level = level;
    }

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
