namespace Planvexa.Modules.Notifications.Application;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.Modules.Notifications.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Compiles and sends a user's digest email of unread inbox items for one workspace, then advances
/// <see cref="DigestPreference.LastSentAtUtc"/> so the same items are not re-sent next run. Invoked by
/// the host background worker under a bound workspace context (mirrors Governance's RetentionRunner).
///
/// Permission-filtered at COMPILE time, not just at the time the original notification was raised: a
/// recipient may have lost access to a resource (e.g. a task moved into a private list) between the
/// notification firing and the digest running, so every candidate item is re-checked here via
/// <see cref="IResourcePermissionQuery"/> — the same per-resource ACL check every other cross-resource
/// listing in this codebase is required to apply (see security note). An item the
/// recipient can no longer read is silently dropped from the digest, not just hidden in the UI.
/// </summary>
public sealed class DigestRunner(
    IDigestPreferenceStore digestPreferences,
    INotificationStore notifications,
    IResourcePermissionQuery resourcePermissions,
    IEmailSender emailSender,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    private const int MaxCandidatesPerDigest = 200;

    /// <summary>Lists every workspace+user digest preference for the worker to iterate (cross-workspace read).</summary>
    public Task<IReadOnlyList<DigestPreference>> ListEnabledAsync(CancellationToken ct = default)
        => digestPreferences.ListEnabledAsync(ct);

    /// <summary>
    /// Compiles and sends the digest for one preference if it is due and has permission-visible content.
    /// Always advances <see cref="DigestPreference.LastSentAtUtc"/> when due, whether or not an email was
    /// sent, so an empty period does not get re-scanned every poll. Returns the number of items included.
    /// </summary>
    public async Task<int> RunAsync(DigestPreference preference, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        if (!preference.IsDue(now))
        {
            return 0;
        }

        var candidates = await notifications.ListForRecipientAsync(
            preference.WorkspaceId, preference.UserId, unreadOnly: true, MaxCandidatesPerDigest, ct);

        var visible = new List<Notification>();
        foreach (var notification in candidates)
        {
            // Notification.EntityType is the display/event convention ("Task", set by e.g. CommentService)
            // — the ACL resource_type vocabulary every IResourceHierarchyQuery provider registers under is
            // lowercase (WorkResourceTypes.Task = "task", see CommentSearchProvider's identical mapping).
            // Passing the capitalized form straight through silently matched no provider and made every
            // item fail-closed (excluded) regardless of real access — this normalizes it instead.
            var resourceType = notification.EntityType.ToLowerInvariant();
            var level = await resourcePermissions.GetEffectiveAsync(
                preference.WorkspaceId, preference.UserId, resourceType, notification.EntityId, ct);
            if (level is not null)
            {
                visible.Add(notification);
            }
        }

        if (visible.Count > 0)
        {
            var (subject, body) = Render(preference.Frequency, visible);
            await emailSender.SendAsync(preference.UserId, subject, body, ct);
        }

        preference.MarkSent(now);
        await unitOfWork.SaveChangesAsync(ct);
        return visible.Count;
    }

    private static (string Subject, string Body) Render(DigestFrequency frequency, IReadOnlyList<Notification> items)
    {
        var subject = $"Your {(frequency == DigestFrequency.Daily ? "daily" : "weekly")} Planvexa digest ({items.Count} unread)";
        var lines = items
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => $"- [{n.EventType}] {n.EntityType} {n.EntityId} ({n.CreatedAtUtc:u})");
        var body = $"You have {items.Count} unread notification(s):\n\n{string.Join('\n', lines)}";
        return (subject, body);
    }
}
