namespace Planvexa.SharedContracts.Users;

/// <summary>Minimal cross-module view of a platform user.</summary>
public sealed record UserInfo(Guid UserId, string Email, string DisplayName);

/// <summary>
/// Contract exposed by the Identity module so other modules can resolve users without touching
/// the Identity tables directly (AGENTS.md rule 7). Implemented in Planvexa.Modules.Identity.
/// </summary>
public interface IUserDirectory
{
    /// <summary>Finds or provisions the application user for an authenticated external subject.</summary>
    Task<UserInfo> GetOrProvisionAsync(
        string subject,
        string email,
        string displayName,
        CancellationToken cancellationToken = default);

    Task<UserInfo?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<UserInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
}
