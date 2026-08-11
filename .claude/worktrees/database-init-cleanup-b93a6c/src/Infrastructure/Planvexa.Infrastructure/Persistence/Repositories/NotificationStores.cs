namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Notifications.Application;
using Planvexa.Modules.Notifications.Domain;

internal sealed class NotificationStore(PlanvexaDbContext db) : INotificationStore
{
    public void Add(Notification notification) => db.Set<Notification>().Add(notification);

    public Task<Notification?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
        => db.Set<Notification>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == id, ct);

    public Task<bool> ExistsAsync(Guid workspaceId, Guid recipientUserId, string deduplicationKey, CancellationToken ct = default)
        => db.Set<Notification>().AnyAsync(
            x => x.WorkspaceId == workspaceId && x.RecipientUserId == recipientUserId && x.DeduplicationKey == deduplicationKey, ct);

    public async Task<IReadOnlyList<Notification>> ListForRecipientAsync(Guid workspaceId, Guid recipientUserId, bool unreadOnly, int max, CancellationToken ct = default)
    {
        var query = db.Set<Notification>().Where(x => x.WorkspaceId == workspaceId && x.RecipientUserId == recipientUserId);
        if (unreadOnly)
        {
            query = query.Where(x => x.ReadAtUtc == null);
        }

        return await query.OrderByDescending(x => x.CreatedAtUtc).Take(max).ToListAsync(ct);
    }

    public Task<int> UnreadCountAsync(Guid workspaceId, Guid recipientUserId, CancellationToken ct = default)
        => db.Set<Notification>().CountAsync(x => x.WorkspaceId == workspaceId && x.RecipientUserId == recipientUserId && x.ReadAtUtc == null, ct);

    public async Task<IReadOnlyList<Notification>> ListUnreadForMarkAllAsync(Guid workspaceId, Guid recipientUserId, CancellationToken ct = default)
        => await db.Set<Notification>()
            .Where(x => x.WorkspaceId == workspaceId && x.RecipientUserId == recipientUserId && x.ReadAtUtc == null)
            .ToListAsync(ct);
}

internal sealed class NotificationPreferenceStore(PlanvexaDbContext db) : INotificationPreferenceStore
{
    public void Add(NotificationPreference preference) => db.Set<NotificationPreference>().Add(preference);

    public Task<NotificationPreference?> FindAsync(Guid workspaceId, Guid userId, string eventType, CancellationToken ct = default)
        => db.Set<NotificationPreference>().FirstOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.UserId == userId && x.EventType == eventType, ct);

    public async Task<IReadOnlyList<NotificationPreference>> ListForUserAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
        => await db.Set<NotificationPreference>().Where(x => x.WorkspaceId == workspaceId && x.UserId == userId)
            .OrderBy(x => x.EventType).ToListAsync(ct);
}

internal sealed class NotificationDeliveryStore(PlanvexaDbContext db) : INotificationDeliveryStore
{
    public async Task<IReadOnlyList<NotificationDelivery>> ListPendingAsync(int max, CancellationToken ct = default)
        => await db.Set<NotificationDelivery>().IgnoreQueryFilters()
            .Where(x => x.Status == DeliveryStatus.Pending)
            .OrderBy(x => x.CreatedAtUtc).Take(max).ToListAsync(ct);

    public Task<Notification?> FindByDeliveryAsync(Guid notificationId, CancellationToken ct = default)
        => db.Set<Notification>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == notificationId, ct);
}

internal sealed class DigestPreferenceStore(PlanvexaDbContext db) : IDigestPreferenceStore
{
    public void Add(DigestPreference preference) => db.Set<DigestPreference>().Add(preference);

    public Task<DigestPreference?> FindAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
        => db.Set<DigestPreference>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId, ct);

    public async Task<IReadOnlyList<DigestPreference>> ListEnabledAsync(CancellationToken ct = default)
        => await db.Set<DigestPreference>().IgnoreQueryFilters()
            .Where(x => x.Frequency != DigestFrequency.Off)
            .ToListAsync(ct);
}
