namespace Planvexa.SharedContracts.Platform;

/// <summary>
/// Whether the identity provider itself will let a new person create an account.
/// </summary>
/// <param name="Manageable">
/// True when Planvexa holds credentials to read and change the setting. False means the operator must
/// change it in the identity provider's own console — <see cref="RegistrationAllowed"/> is then
/// whatever we last managed to observe, or null if we cannot see it at all.
/// </param>
/// <param name="RegistrationAllowed">
/// The identity provider's current state, or null when it could not be determined.
/// </param>
/// <param name="Detail">Operator-facing explanation when something is not manageable or failed.</param>
public sealed record IdentityProviderRegistrationState(
    bool Manageable,
    bool? RegistrationAllowed,
    string? Detail);

/// <summary>
/// Planvexa's self-registration setting has always had two halves, and only one of them lived here.
/// The host console's toggle governs whether Planvexa ACCEPTS a new identity
/// (<c>UserDirectory.GetOrProvisionAsync</c>'s gate); the identity provider separately governs whether
/// an account can be CREATED at all. With Keycloak's realm registration disabled, turning the toggle on
/// changes nothing a user can see — the sign-up link just fails — which makes the setting look broken.
///
/// This contract closes that gap: when the operator supplies identity-provider admin credentials, the
/// toggle drives both halves. When they do not, the console reports the mismatch instead of silently
/// promising something it cannot deliver.
///
/// Implemented in the API host (it is an outbound HTTP integration, not domain logic), with a no-op
/// implementation registered when no credentials are configured.
/// </summary>
public interface IIdentityProviderRegistration
{
    Task<IdentityProviderRegistrationState> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Best-effort. Never throws: a failure to reach the identity provider is reported through the
    /// returned state, because it must not roll back a settings change that has already been saved.
    /// </summary>
    Task<IdentityProviderRegistrationState> SetAsync(bool allowed, CancellationToken cancellationToken = default);
}
