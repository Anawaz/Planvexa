namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Integrations.Application;
using Planvexa.Modules.Integrations.Domain;

internal sealed class WebhookSubscriptionStore(PlanvexaDbContext db) : IWebhookSubscriptionStore
{
    public void Add(WebhookSubscription subscription) => db.Set<WebhookSubscription>().Add(subscription);

    public Task<WebhookSubscription?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<WebhookSubscription>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<WebhookSubscription>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<WebhookSubscription>()
            .Where(x => x.WorkspaceId == workspaceId && x.IsActive)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);

    public async Task<IReadOnlyList<WebhookSubscription>> ListActiveForEventAsync(Guid workspaceId, string eventType, CancellationToken ct = default)
    {
        // EventTypesCsv is a small comma-joined list; filter in memory after the workspace/active cut.
        var candidates = await db.Set<WebhookSubscription>()
            .Where(x => x.WorkspaceId == workspaceId && x.IsActive)
            .ToListAsync(ct);
        return candidates.Where(s => s.IsSubscribedTo(eventType)).ToList();
    }
}

internal sealed class WebhookDeliveryStore(PlanvexaDbContext db) : IWebhookDeliveryStore
{
    public void Add(WebhookDelivery delivery) => db.Set<WebhookDelivery>().Add(delivery);

    public Task<bool> ExistsAsync(Guid subscriptionId, Guid eventId, CancellationToken ct = default)
        => db.Set<WebhookDelivery>().AnyAsync(x => x.SubscriptionId == subscriptionId && x.EventId == eventId, ct);

    public async Task<IReadOnlyList<WebhookDelivery>> ListBySubscriptionAsync(Guid subscriptionId, int max, CancellationToken ct = default)
        => await db.Set<WebhookDelivery>()
            .Where(x => x.SubscriptionId == subscriptionId)
            .OrderByDescending(x => x.OccurredAtUtc).Take(max).ToListAsync(ct);

    public Task<WebhookDelivery?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<WebhookDelivery>().FirstOrDefaultAsync(x => x.Id == id, ct);
}

internal sealed class PersonalAccessTokenStore(PlanvexaDbContext db, MaintenanceConnection maintenance) : IPersonalAccessTokenStore
{
    public void Add(PersonalAccessToken token) => db.Set<PersonalAccessToken>().Add(token);

    public void Remove(PersonalAccessToken token) => db.Set<PersonalAccessToken>().Remove(token);

    public Task<PersonalAccessToken?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
        => db.Set<PersonalAccessToken>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id, ct);

    // PAT authentication runs before any workspace context is established: the token hash is globally
    // unique and proves the owning workspace, so the workspace query filter is bypassed for this lookup.
    public Task<PersonalAccessToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default)
        => maintenance.LookupAsync(db, () => db.Set<PersonalAccessToken>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct));

    public async Task<IReadOnlyList<PersonalAccessToken>> ListForUserAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
        => await db.Set<PersonalAccessToken>()
            .Where(x => x.WorkspaceId == workspaceId && x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
}

internal sealed class OAuthApplicationStore(PlanvexaDbContext db, MaintenanceConnection maintenance) : IOAuthApplicationStore
{
    public void Add(OAuthApplication application) => db.Set<OAuthApplication>().Add(application);

    public Task<OAuthApplication?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
        => db.Set<OAuthApplication>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id, ct);

    // Token-endpoint lookups (authorize/token/refresh) run before any workspace context is
    // established — client_id is globally unique and the request itself proves the workspace via the
    // application it resolves to, mirroring PersonalAccessTokenStore.FindByHashAsync.
    public Task<OAuthApplication?> FindByClientIdAsync(string clientId, CancellationToken ct = default)
        => maintenance.LookupAsync(db, () => db.Set<OAuthApplication>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.ClientId == clientId, ct));

    public async Task<IReadOnlyList<OAuthApplication>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<OAuthApplication>()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
}

internal sealed class OAuthAuthorizationCodeStore(PlanvexaDbContext db, MaintenanceConnection maintenance) : IOAuthAuthorizationCodeStore
{
    public void Add(OAuthAuthorizationCode code) => db.Set<OAuthAuthorizationCode>().Add(code);

    public Task<OAuthAuthorizationCode?> FindByHashAsync(string codeHash, CancellationToken ct = default)
        => maintenance.LookupAsync(db, () => db.Set<OAuthAuthorizationCode>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.CodeHash == codeHash, ct));
}

internal sealed class OAuthTokenStore(PlanvexaDbContext db, MaintenanceConnection maintenance) : IOAuthTokenStore
{
    public void Add(OAuthToken token) => db.Set<OAuthToken>().Add(token);

    public Task<OAuthToken?> FindByAccessTokenHashAsync(string accessTokenHash, CancellationToken ct = default)
        => maintenance.LookupAsync(db, () => db.Set<OAuthToken>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.AccessTokenHash == accessTokenHash, ct));

    public Task<OAuthToken?> FindByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct = default)
        => maintenance.LookupAsync(db, () => db.Set<OAuthToken>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.RefreshTokenHash == refreshTokenHash, ct));
}

internal sealed class IntegrationProviderSettingsStore(PlanvexaDbContext db) : IIntegrationProviderSettingsStore
{
    public void Add(IntegrationProviderSettings settings) => db.Set<IntegrationProviderSettings>().Add(settings);

    public Task<IntegrationProviderSettings?> FindAsync(Guid workspaceId, string provider, CancellationToken ct = default)
        => db.Set<IntegrationProviderSettings>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Provider == provider, ct);

    public async Task<IReadOnlyList<IntegrationProviderSettings>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<IntegrationProviderSettings>().Where(x => x.WorkspaceId == workspaceId).ToListAsync(ct);
}
