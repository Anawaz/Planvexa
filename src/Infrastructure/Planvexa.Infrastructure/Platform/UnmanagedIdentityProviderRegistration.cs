namespace Planvexa.Infrastructure.Platform;

using Planvexa.SharedContracts.Platform;

/// <summary>
/// Registered when no identity-provider admin credentials are configured. Reports "not manageable" so
/// the host console can say plainly that the toggle governs Planvexa only, and what to do about it —
/// rather than appearing to control something it does not.
/// </summary>
public sealed class UnmanagedIdentityProviderRegistration : IIdentityProviderRegistration
{
    private static readonly IdentityProviderRegistrationState State = new(
        Manageable: false,
        RegistrationAllowed: null,
        Detail: "Planvexa has no identity-provider admin credentials, so this setting controls Planvexa only. "
            + "Enable registration in your identity provider as well (for Keycloak: Realm settings → Login → "
            + "User registration), or configure Keycloak:AdminClientId/AdminClientSecret to let Planvexa manage it.");

    public Task<IdentityProviderRegistrationState> GetAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(State);

    public Task<IdentityProviderRegistrationState> SetAsync(bool allowed, CancellationToken cancellationToken = default)
        => Task.FromResult(State);
}
