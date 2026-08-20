namespace Planvexa.Infrastructure.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Platform;
using Planvexa.Infrastructure.Persistence;
using Planvexa.SharedContracts.Platform;

public sealed record UpdateInstanceSettingsCommand(
    bool? AllowSelfRegistration,
    string? WorkspaceCreationPolicy,
    string? InstanceName,
    string? LogoUrl,
    string? SupportEmail);

/// <summary>
/// Reads and writes the single installation-wide settings row, and implements the module-facing
/// <see cref="IInstanceSettingsProvider"/> contract.
///
/// Read path is on the hot path — <c>UserDirectory.GetOrProvisionAsync</c> runs on EVERY
/// authenticated request — so the snapshot is memoised for the process. The cache is invalidated on
/// write, which is correct for the single-replica case and eventually consistent (bounded by
/// <see cref="CacheDuration"/>) across replicas; a setting taking up to a minute to reach a second
/// replica is a fine trade for not adding a query to every request.
/// ponytail: process-local cache; if these ever need to change instantly fleet-wide, publish an
///  invalidation through the existing outbox rather than shortening the window.
/// </summary>
public sealed class InstanceSettingsService(
    PlanvexaDbContext db,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IClock clock,
    IConfiguration configuration,
    InstanceSettingsCache cache,
    IIdentityProviderRegistration identityProvider) : IInstanceSettingsProvider
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    public async Task<InstanceSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGet(clock.UtcNow, out var cached))
        {
            return cached;
        }

        var snapshot = ToSnapshot(await LoadAsync(cancellationToken));
        cache.Set(snapshot, clock.UtcNow);
        return snapshot;
    }

    /// <summary>The full row for the host console, including who last changed it.</summary>
    public async Task<InstanceSettings> GetForAdminAsync(CancellationToken cancellationToken = default)
        => await LoadAsync(cancellationToken);

    public async Task<InstanceSettings> UpdateAsync(
        UpdateInstanceSettingsCommand command, CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);
        settings.Update(
            command.AllowSelfRegistration,
            command.WorkspaceCreationPolicy,
            command.InstanceName,
            command.LogoUrl,
            command.SupportEmail,
            currentUser.IsAuthenticated ? currentUser.UserId : null,
            clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        cache.Invalidate();

        // Push the identity provider's half of the same setting. AFTER the commit and deliberately
        // never allowed to fail the save: Planvexa's gate is the authoritative one, and an unreachable
        // identity provider must not lose a change the operator already made. The outcome is surfaced
        // by GET /host/settings so a failed sync is visible rather than silent.
        if (command.AllowSelfRegistration is { } allowSelfRegistration)
        {
            LastIdentityProviderSync = await identityProvider.SetAsync(allowSelfRegistration, cancellationToken);
        }

        return settings;
    }

    /// <summary>
    /// The result of the most recent identity-provider sync in THIS request, or null when the update
    /// did not touch self-registration. Scoped state, not shared: the settings endpoint reads it
    /// immediately after calling <see cref="UpdateAsync"/>.
    /// </summary>
    public IdentityProviderRegistrationState? LastIdentityProviderSync { get; private set; }

    /// <summary>Current identity-provider registration state, for the console to display.</summary>
    public Task<IdentityProviderRegistrationState> GetIdentityProviderStateAsync(CancellationToken cancellationToken = default)
        => identityProvider.GetAsync(cancellationToken);

    /// <summary>
    /// Loads the singleton row, creating it on first read. Script 0095 deliberately creates the table
    /// without seeding the row so that this — the only place that can read configuration — decides the
    /// initial <c>AllowSelfRegistration</c>. An installation that had
    /// <c>Registration:AllowSelfRegistration=false</c> therefore keeps it false through the upgrade;
    /// after this first read the row owns the value and the configuration key is only the default for
    /// the next fresh install.
    /// </summary>
    private async Task<InstanceSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await FindAsync(cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        var allowSelfRegistration =
            !bool.TryParse(configuration["Registration:AllowSelfRegistration"], out var configured) || configured;
        settings = BuildingBlocks.Platform.InstanceSettings.CreateDefault(allowSelfRegistration);
        db.InstanceSettings.Add(settings);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return settings;
        }
        catch (DbUpdateException)
        {
            // Startup fires several authenticated requests at once and every one of them reads these
            // settings, so two can both find no row before either commits. The primary key makes the
            // loser's insert fail rather than duplicating the singleton — same shape as
            // UserDirectory's concurrent-provision race. Adopt whichever row won.
            db.Entry(settings).State = EntityState.Detached;
            return await FindAsync(cancellationToken)
                ?? throw new InvalidOperationException("Instance settings could not be created or read.");
        }
    }

    private Task<InstanceSettings?> FindAsync(CancellationToken cancellationToken)
        => db.InstanceSettings
            .FirstOrDefaultAsync(s => s.Id == BuildingBlocks.Platform.InstanceSettings.SingletonId, cancellationToken);

    public static InstanceSettingsSnapshot ToSnapshot(InstanceSettings settings) => new(
        settings.AllowSelfRegistration,
        settings.WorkspaceCreationPolicy,
        settings.InstanceName,
        settings.LogoUrl,
        settings.SupportEmail);
}

/// <summary>
/// Process-wide memo for <see cref="InstanceSettingsService"/>. A singleton (the service itself is
/// scoped, like everything that touches the DbContext), holding a value type only — never an entity,
/// which would leak a DbContext-tracked instance across scopes.
/// </summary>
public sealed class InstanceSettingsCache
{
    private readonly Lock _gate = new();
    private InstanceSettingsSnapshot? _snapshot;
    private DateTimeOffset _expiresAtUtc;

    public bool TryGet(DateTimeOffset nowUtc, out InstanceSettingsSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_snapshot is not null && nowUtc < _expiresAtUtc)
            {
                snapshot = _snapshot;
                return true;
            }
        }

        snapshot = null!;
        return false;
    }

    public void Set(InstanceSettingsSnapshot snapshot, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            _snapshot = snapshot;
            _expiresAtUtc = nowUtc.Add(InstanceSettingsService.CacheDuration);
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _snapshot = null;
        }
    }
}
