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

    /// <summary>
    /// Instance-level (NOT Workspace-level) administrator: may use the host administration console to
    /// manage every Workspace and account in this installation. Deliberately a property of the global
    /// <see cref="User"/> rather than a Workspace membership role, because the host administrator
    /// administers the installation, not any particular Workspace — they are typically a member of
    /// none of them. Backed by identity.users.is_host_admin (script 0094), which the host-admin RLS
    /// policies re-check in the database so this is not an application-layer-only guarantee.
    /// </summary>
    public bool IsHostAdmin { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public DateTimeOffset? LastSeenAtUtc { get; private set; }

    /// <summary>
    /// Relative API path serving the user's uploaded profile picture (e.g. <c>/users/{id}/avatar</c>),
    /// set by <see cref="SetAvatarUrl"/> — never a raw storage path or presigned URL. Null until the user
    /// uploads one; the frontend falls back to rendering initials from <see cref="DisplayName"/>.
    /// </summary>
    public string? AvatarUrl { get; private set; }

    /// <summary>
    /// True once <see cref="UpdateDisplayName"/> has been called (a user-initiated rename). Every
    /// authenticated request re-provisions via <see cref="SyncProfile"/> with the IdP's current claims
    /// (see UserContextMiddleware/UserDirectory.GetOrProvisionAsync) — without this flag that sync would
    /// silently overwrite a self-service rename back to the IdP-supplied name on the very next request.
    /// Email is still synced from the IdP regardless (email is IdP-owned, not user-editable here).
    /// </summary>
    public bool HasCustomDisplayName { get; private set; }

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

    /// <summary>IANA timezone id (e.g. "America/New_York"), or null to use the browser's ambient timezone.</summary>
    public string? Timezone { get; private set; }

    /// <summary>BCP 47 locale tag (e.g. "en-US"), or null to use the browser's default locale.</summary>
    public string? Locale { get; private set; }

    /// <summary>"light", "dark", or "system" (follow OS preference), or null to use the pre-auth
    /// localStorage/browser-ambient fallback (see ThemeContext in apps/web).</summary>
    public string? Theme { get; private set; }

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
        // Once the user has renamed themselves (HasCustomDisplayName), the IdP's name claim no longer
        // wins — otherwise this sync (which runs on every authenticated request) would clobber a
        // self-service rename back to the IdP value on the next request.
        var displayNameChanged = !HasCustomDisplayName && DisplayName != normalizedName;
        if (Email == normalizedEmail && !displayNameChanged)
        {
            return;
        }

        Email = normalizedEmail;
        if (!HasCustomDisplayName)
        {
            DisplayName = normalizedName;
        }

        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// User-initiated rename (self-service profile edit). Unlike <see cref="SyncProfile"/> (IdP-driven,
    /// also updates Email), this only ever touches DisplayName. Max length matches the column
    /// (<see cref="Infrastructure.UserConfiguration"/>, 200 chars).
    /// </summary>
    public void UpdateDisplayName(string displayName, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        var normalized = displayName.Trim();
        if (normalized.Length > 200)
        {
            throw new ArgumentException($"'{nameof(displayName)}' must not exceed 200 characters.", nameof(displayName));
        }

        HasCustomDisplayName = true;
        if (DisplayName == normalized)
        {
            return;
        }

        DisplayName = normalized;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Self-service display preferences (timezone/locale/theme). Any of them can be cleared back to
    /// "use browser ambient" by passing null/blank — same normalize-to-null convention as the rest of
    /// this entity. <paramref name="theme"/> must be "light", "dark", or "system" (case-insensitive);
    /// the request-level validator (UpdateDisplayNameRequestValidator) is what actually rejects bad
    /// values before this runs — this is just a defensive fallback.
    /// </summary>
    public void SetPreferences(string? timezone, string? locale, string? theme, DateTimeOffset nowUtc)
    {
        var normalizedTimezone = string.IsNullOrWhiteSpace(timezone) ? null : timezone.Trim();
        var normalizedLocale = string.IsNullOrWhiteSpace(locale) ? null : locale.Trim();
        var normalizedTheme = string.IsNullOrWhiteSpace(theme) ? null : theme.Trim().ToLowerInvariant();
        if (normalizedTheme is not null && normalizedTheme is not ("light" or "dark" or "system"))
        {
            throw new ArgumentException("Theme must be 'light', 'dark', or 'system'.", nameof(theme));
        }

        if (Timezone == normalizedTimezone && Locale == normalizedLocale && Theme == normalizedTheme)
        {
            return;
        }

        Timezone = normalizedTimezone;
        Locale = normalizedLocale;
        Theme = normalizedTheme;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkSeen(DateTimeOffset nowUtc) => LastSeenAtUtc = nowUtc;

    /// <summary>
    /// Host-administrator account suspension. Unlike <see cref="Anonymize"/> (which also clears
    /// <see cref="IsActive"/>) this is fully reversible and destroys nothing — it only closes the door.
    /// Enforcement lives in <see cref="Application.UserDirectory.GetOrProvisionAsync(string, string,
    /// string, bool, CancellationToken)"/>, the single path every authenticated HTTP request and
    /// SignalR connection passes through, so a deactivated account loses access everywhere at once
    /// rather than per-endpoint. Idempotent.
    /// </summary>
    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Reverses <see cref="Deactivate"/>. Refuses to resurrect an anonymized account: its PII is gone
    /// (see <see cref="IsAnonymized"/>), so "reactivating" it would only produce a login-less shell
    /// under a scrubbed subject.
    /// </summary>
    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsAnonymized)
        {
            throw new InvalidOperationException("An anonymized account cannot be reactivated.");
        }

        if (IsActive)
        {
            return;
        }

        IsActive = true;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Grants instance-level administration (see <see cref="IsHostAdmin"/>). Idempotent.</summary>
    public void GrantHostAdmin(DateTimeOffset nowUtc)
    {
        if (IsHostAdmin)
        {
            return;
        }

        IsHostAdmin = true;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Revokes instance-level administration. The "you cannot demote the last host admin (or
    /// yourself)" rules are enforced by the caller, which is the only layer that can see the other
    /// accounts — this method just flips the flag. Idempotent.
    /// </summary>
    public void RevokeHostAdmin(DateTimeOffset nowUtc)
    {
        if (!IsHostAdmin)
        {
            return;
        }

        IsHostAdmin = false;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Self-service avatar upload (see AvatarService). Only ever called with a path this
    /// server just wrote to storage, never a client-supplied URL.</summary>
    public void SetAvatarUrl(string avatarUrl, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(avatarUrl, nameof(avatarUrl));
        AvatarUrl = avatarUrl;
        UpdatedAtUtc = nowUtc;
    }

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
        // The blob itself is left in storage (best-effort cleanup, same tradeoff every attachment-delete
        // path in this codebase accepts) — clearing the pointer is what stops it from being served.
        AvatarUrl = null;
        IsActive = false;
        // A scrubbed account must not keep instance-level administration: IsActive alone would stop it
        // signing in, but the last-host-admin guard counts flagged rows, and a deleted account left
        // flagged would keep blocking the real host admin from ever being demoted.
        IsHostAdmin = false;
        IsAnonymized = true;
        AnonymizedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }
}
