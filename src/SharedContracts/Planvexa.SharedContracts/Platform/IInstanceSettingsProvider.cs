namespace Planvexa.SharedContracts.Platform;

/// <summary>The installation-wide settings a module needs to see, as a value.</summary>
public sealed record InstanceSettingsSnapshot(
    bool AllowSelfRegistration,
    string WorkspaceCreationPolicy,
    string? InstanceName,
    string? LogoUrl,
    string? SupportEmail);

/// <summary>
/// Read access to the installation's settings for modules that must honour them —
/// Identity (self-registration) and Tenancy (who may create a Workspace). The row itself lives in the
/// <c>platform</c> schema and is owned by Infrastructure, so this contract is what keeps those modules
/// from reaching outside their own tables (AGENTS.md rule 7).
///
/// Read-only on purpose: only the host console writes these, and it does so through Infrastructure
/// directly. Nothing in a module has any business changing an installation-wide setting.
/// </summary>
public interface IInstanceSettingsProvider
{
    Task<InstanceSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default);
}
