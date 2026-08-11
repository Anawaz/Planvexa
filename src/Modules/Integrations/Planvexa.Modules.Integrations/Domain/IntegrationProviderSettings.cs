namespace Planvexa.Modules.Integrations.Domain;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// A workspace's configuration for one third-party integration provider — one row per (workspace,
/// provider), following the exact shape of <c>AiProviderSettings</c> (optional, workspace-configured,
/// encrypted secret, enable/disable toggle, graceful no-op when disabled/unconfigured). A single generic
/// entity serves every provider in <see cref="IntegrationProviders"/> rather than one near-identical
/// table per provider (AGENTS.md rule 16: prefer the existing shape over 11 near-duplicate schemas) —
/// <see cref="ConfigJson"/> holds the provider's non-secret fields (e.g. GitHub's owner/repo) and
/// <see cref="SecretEncrypted"/> holds the one sensitive credential (Slack's webhook URL, GitHub's PAT,
/// ...), encrypted the same way <c>AiProviderSettings.ApiKeyEncrypted</c> is. The secret is write-only
/// over the API: reads return a masked hint via <see cref="AiProviderSettings"/>-style masking (reused
/// from <see cref="SecretCrypto"/>'s sibling <c>PersonalAccessToken</c> pattern is not applicable here —
/// masking lives in the application service, matching AiSettingsService).
/// </summary>
public sealed class IntegrationProviderSettings : Entity, IWorkspaceOwned
{
    private IntegrationProviderSettings()
    {
    }

    private IntegrationProviderSettings(Guid id, Guid workspaceId, string provider, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Provider = provider;
        ConfigJson = "{}";
        SecretEncrypted = string.Empty;
        UpdatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string ConfigJson { get; private set; } = "{}";
    public string SecretEncrypted { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>True when the workspace should route calls to this provider — mirrors
    /// <c>AiProviderSettings.IsUsable</c>: enabled AND has at least a secret configured.</summary>
    public bool IsUsable => IsEnabled && SecretEncrypted.Length > 0;

    public static IntegrationProviderSettings CreateDefault(Guid id, Guid workspaceId, string provider, DateTimeOffset nowUtc)
    {
        if (!IntegrationProviders.All.Contains(provider))
        {
            throw new ValidationAppException($"Unknown integration provider '{provider}'.");
        }

        return new IntegrationProviderSettings(id, workspaceId, provider, nowUtc);
    }

    /// <summary><paramref name="secretEncrypted"/> is null when the caller did not supply a new secret,
    /// in which case the stored one is kept (write-only secret semantics, matching AiProviderSettings).</summary>
    public void Update(string configJson, string? secretEncrypted, bool isEnabled, DateTimeOffset nowUtc)
    {
        ConfigJson = string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson;
        if (secretEncrypted is not null)
        {
            SecretEncrypted = secretEncrypted;
        }

        if (isEnabled && SecretEncrypted.Length == 0)
        {
            throw new ValidationAppException("A credential is required before enabling this integration.");
        }

        IsEnabled = isEnabled;
        UpdatedAtUtc = nowUtc;
    }
}

/// <summary>
/// The full target-integration catalogue (). <see cref="RealImplementation"/> marks
/// the providers with a real (mockable, tested) protocol call behind them
/// (<c>SlackClient</c>/<c>GitHubClient</c>) — every other provider has settings scaffolding only: it can
/// be configured and enabled/disabled, but no outbound call is wired up yet (a clearly-stated gap, never
/// a faked success response).
/// </summary>
public static class IntegrationProviders
{
    public const string Slack = "slack";
    public const string GitHub = "github";
    public const string GoogleCalendar = "google_calendar";
    public const string OutlookCalendar = "outlook_calendar";
    public const string MicrosoftTeams = "teams";
    public const string GitLab = "gitlab";
    public const string GoogleDrive = "google_drive";
    public const string OneDrive = "onedrive";
    public const string SharePoint = "sharepoint";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Slack, GitHub, GoogleCalendar, OutlookCalendar, MicrosoftTeams, GitLab, GoogleDrive, OneDrive, SharePoint,
    };

    public static readonly IReadOnlySet<string> RealImplementation = new HashSet<string> { Slack, GitHub };
}
