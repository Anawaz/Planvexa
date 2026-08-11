namespace Planvexa.SharedContracts.Users;

/// <summary>Minimal cross-module view of a platform user.</summary>
public sealed record UserInfo(
    Guid UserId,
    string Email,
    string DisplayName,
    string? AvatarUrl = null,
    string? Timezone = null,
    string? Locale = null,
    string? Theme = null);

/// <summary>
/// Contract exposed by the Identity module so other modules can resolve users without touching
/// the Identity tables directly (AGENTS.md rule 7). Implemented in Planvexa.Modules.Identity.
/// </summary>
public interface IUserDirectory
{
    /// <summary>Finds or provisions the application user for an authenticated external subject. Subject
    /// to the self-registration gate (<c>Registration:AllowSelfRegistration</c>) for brand-new users.</summary>
    Task<UserInfo> GetOrProvisionAsync(
        string subject,
        string email,
        string displayName,
        CancellationToken cancellationToken = default);

    /// <summary>Same as above, but lets trusted, config-driven callers (e.g. the first-run bootstrap
    /// admin) skip the self-registration gate by passing <paramref name="enforceRegistrationGate"/>
    /// as <see langword="false"/>.</summary>
    Task<UserInfo> GetOrProvisionAsync(
        string subject,
        string email,
        string displayName,
        bool enforceRegistrationGate,
        CancellationToken cancellationToken = default);

    Task<UserInfo?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<UserInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
}
