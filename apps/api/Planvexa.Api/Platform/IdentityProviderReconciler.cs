namespace Planvexa.Api.Platform;

using Planvexa.SharedContracts.Platform;

/// <summary>
/// Brings the identity provider's registration flag into line with this instance's setting once at
/// start-up.
///
/// Without this, the two halves of self-registration only converge when a host administrator happens
/// to touch the toggle — so an instance whose Keycloak realm was created (or restored, or hand-edited)
/// with registration disabled keeps rejecting sign-ups while the console insists it is on, and the only
/// clue is a 400 from <c>/registrations</c>. Reconciling on every start makes Planvexa's stored setting
/// the durable source of truth rather than something that has to be re-applied by hand.
///
/// Only ever acts when the operator has configured identity-provider admin credentials; otherwise
/// <see cref="IIdentityProviderRegistration"/> is the no-op implementation, this logs the mismatch it
/// cannot fix, and the console shows the same thing. Never throws — a start-up path must not be able to
/// take the API down because an identity provider was slow.
/// </summary>
public sealed class IdentityProviderReconciler(
    IServiceScopeFactory scopeFactory,
    ILogger<IdentityProviderReconciler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // After DbUp and the first-run bootstrap: the settings row may not exist until those finish.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(12), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<Infrastructure.Platform.InstanceSettingsService>();
            var identityProvider = scope.ServiceProvider.GetRequiredService<IIdentityProviderRegistration>();

            var desired = (await settings.GetAsync(stoppingToken)).AllowSelfRegistration;
            var current = await identityProvider.GetAsync(stoppingToken);

            if (current.RegistrationAllowed == desired)
            {
                return;
            }

            if (!current.Manageable)
            {
                // Worth a warning rather than silence: this is the exact state in which the toggle looks
                // broken to whoever set it.
                if (desired)
                {
                    logger.LogWarning(
                        "Self-registration is enabled in Planvexa, but Planvexa cannot manage the identity "
                        + "provider, so sign-up may still be refused there. {Detail}", current.Detail);
                }

                return;
            }

            var result = await identityProvider.SetAsync(desired, stoppingToken);
            if (result.RegistrationAllowed == desired)
            {
                logger.LogInformation(
                    "Reconciled identity-provider registration to {Allowed} to match this instance's setting.", desired);
            }
            else
            {
                logger.LogWarning(
                    "Could not reconcile identity-provider registration to {Allowed}. {Detail}", desired, result.Detail);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Identity-provider registration reconciliation failed.");
        }
    }
}
