namespace Planvexa.Modules.Identity.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// A platform user. Users are NOT tenant-owned — a single person may belong to many tenants
/// through workspace memberships. Identity is proven by Keycloak (<see cref="Subject"/>).
/// </summary>
public sealed class User : Entity, IAggregateRoot
{
    private User()
    {
    }

    private User(Guid id, string subject, string email, string displayName, DateTimeOffset createdAtUtc)
        : base(id)
    {
        Subject = subject;
        Email = email;
        DisplayName = displayName;
        CreatedAtUtc = createdAtUtc;
        IsActive = true;
    }

    /// <summary>External identity-provider subject (Keycloak 'sub'). Unique.</summary>
    public string Subject { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public DateTimeOffset? LastSeenAtUtc { get; private set; }

    /// <summary>
    /// GDPR-style account deletion: true once <see cref="Anonymize"/> has scrubbed this
    /// row's PII. The row is kept (not hard-deleted) because <see cref="Entity.Id"/> is referenced as an
    /// author/assignee/actor FK across other modules (tasks, comments, time entries, audit events, ...);
    /// hard-deleting it would either break those references or require rewriting every referencing row in
    /// every module. Anonymizing in place is the standard GDPR pattern (keep the row, strip the PII) and
    /// is simpler and safer than repointing every FK to a separate shared sentinel row: each deleted user
    /// keeps their own distinct identity (so two different former members still show as distinct "Deleted
    /// User" entries, not merged into one), and no other module's tables need to be touched at all —
    /// every place that resolves this UserId to a display name automatically shows the scrubbed values.
    /// </summary>
    public bool IsAnonymized { get; private set; }

    public DateTimeOffset? AnonymizedAtUtc { get; private set; }

    public static User Provision(Guid id, string subject, string email, string displayName, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(subject, nameof(subject));
        Guard.AgainstNullOrWhiteSpace(email, nameof(email));
        return new User(id, subject, email.Trim().ToLowerInvariant(), displayName.Trim(), nowUtc);
    }

    /// <summary>
    /// Re-links this user to a new identity-provider subject. Used when a login arrives with an
    /// unknown subject but a known (IdP-verified) email — e.g. seeded development users whose
    /// Keycloak accounts were created after the application user row.
    /// </summary>
    public void LinkSubject(string subject, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(subject, nameof(subject));
        if (Subject == subject)
        {
            return;
        }

        Subject = subject;
        UpdatedAtUtc = nowUtc;
    }

    public void SyncProfile(string email, string displayName, DateTimeOffset nowUtc)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var normalizedName = displayName.Trim();
        if (Email == normalizedEmail && DisplayName == normalizedName)
        {
            return;
        }

        Email = normalizedEmail;
        DisplayName = normalizedName;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkSeen(DateTimeOffset nowUtc) => LastSeenAtUtc = nowUtc;

    /// <summary>
    /// Scrubs PII in place (see <see cref="IsAnonymized"/>'s doc comment for why this is not a hard
    /// delete) and deactivates the account. Idempotent — calling it twice is a no-op the second time.
    /// Subject/Email stay unique (both have unique indexes) by deriving from this row's own id.
    /// </summary>
    public void Anonymize(DateTimeOffset nowUtc)
    {
        if (IsAnonymized)
        {
            return;
        }

        Subject = $"deleted-{Id:N}";
        Email = $"deleted-{Id:N}@deleted.invalid";
        DisplayName = "Deleted User";
        IsActive = false;
        IsAnonymized = true;
        AnonymizedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }
}
