namespace Planvexa.BuildingBlocks.Platform;

/// <summary>
/// Installation-wide settings, editable by a host administrator. Exactly one row exists
/// (<see cref="SingletonId"/>, enforced by a CHECK constraint) — these describe the installation, not
/// a Workspace, so there is nothing to key them by.
///
/// Lives in BuildingBlocks rather than a module for the same reason <c>OutboxMessage</c> does: it
/// belongs to the <c>platform</c> schema and to no bounded context, and it is configured explicitly by
/// <c>PlanvexaDbContext</c> rather than picked up by a module's assembly scan. Being here also lets
/// modules read it through <c>IInstanceSettingsProvider</c> without depending on Infrastructure.
///
/// Not <c>IWorkspaceOwned</c> and deliberately not RLS-protected — same posture as
/// <c>identity.users</c> and <c>platform.outbox_messages</c>. A Workspace-keyed policy would be
/// meaningless for a table with one global row, and the values here (is self-registration open? who
/// may create workspaces? what is this instance called?) are read on paths that have no ambient
/// Workspace at all, including the anonymous landing page.
/// </summary>
public sealed class InstanceSettings
{
    /// <summary>The single row's primary key. A fixed value, not a generated id — see the class doc.</summary>
    public const int SingletonId = 1;

    private InstanceSettings()
    {
    }

    public int Id { get; private set; } = SingletonId;

    /// <summary>
    /// Whether an unknown identity may provision itself an account on first sign-in. Replaces the
    /// <c>Registration:AllowSelfRegistration</c> configuration key as the live value — that key is now
    /// only the seed default for this row. Enforced in <c>UserDirectory.GetOrProvisionAsync</c>.
    /// </summary>
    public bool AllowSelfRegistration { get; private set; }

    /// <summary>
    /// <see cref="WorkspaceCreationPolicies.Anyone"/> or
    /// <see cref="WorkspaceCreationPolicies.HostAdminsOnly"/>. Enforced in
    /// <c>WorkspaceRegistrationService</c> — the single workspace-creation path — rather than at the
    /// endpoint, so every caller is covered.
    /// </summary>
    public string WorkspaceCreationPolicy { get; private set; } = WorkspaceCreationPolicies.Anyone;

    /// <summary>Shown in the shell and on the sign-in page. Null falls back to "Planvexa".</summary>
    public string? InstanceName { get; private set; }

    /// <summary>Absolute URL of the instance logo, or null to render the product wordmark.</summary>
    public string? LogoUrl { get; private set; }

    /// <summary>Where users are told to go for help. Null hides the support link entirely.</summary>
    public string? SupportEmail { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedByUserId { get; private set; }

    public static InstanceSettings CreateDefault(bool allowSelfRegistration) => new()
    {
        Id = SingletonId,
        AllowSelfRegistration = allowSelfRegistration,
        WorkspaceCreationPolicy = WorkspaceCreationPolicies.Anyone,
    };

    /// <summary>
    /// Applies a partial update: a null argument means "leave this one alone", which is what lets the
    /// console's separate forms (access, branding) each submit only their own fields. Blank strings
    /// normalize to null — the "unset, fall back to the default" state — so a cleared text input
    /// actually clears the value rather than storing an empty string.
    /// </summary>
    public void Update(
        bool? allowSelfRegistration,
        string? workspaceCreationPolicy,
        string? instanceName,
        string? logoUrl,
        string? supportEmail,
        Guid? updatedByUserId,
        DateTimeOffset nowUtc)
    {
        if (allowSelfRegistration is { } allow)
        {
            AllowSelfRegistration = allow;
        }

        if (workspaceCreationPolicy is not null)
        {
            WorkspaceCreationPolicy = WorkspaceCreationPolicies.Normalize(workspaceCreationPolicy);
        }

        if (instanceName is not null)
        {
            InstanceName = Blank(instanceName);
        }

        if (logoUrl is not null)
        {
            LogoUrl = Blank(logoUrl);
        }

        if (supportEmail is not null)
        {
            SupportEmail = Blank(supportEmail)?.ToLowerInvariant();
        }

        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = nowUtc;
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>The two values <see cref="InstanceSettings.WorkspaceCreationPolicy"/> may hold.</summary>
public static class WorkspaceCreationPolicies
{
    public const string Anyone = "Anyone";
    public const string HostAdminsOnly = "HostAdminsOnly";

    /// <summary>
    /// Maps arbitrary input onto one of the two known values, defaulting to the permissive one — which
    /// is the pre-existing behaviour, so an unrecognised value can never silently lock workspace
    /// creation for the whole installation. Request validation rejects bad input before this runs; this
    /// is the defensive fallback for a hand-edited database row.
    /// </summary>
    public static string Normalize(string? value)
        => string.Equals(value?.Trim(), HostAdminsOnly, StringComparison.OrdinalIgnoreCase)
            ? HostAdminsOnly
            : Anyone;
}
