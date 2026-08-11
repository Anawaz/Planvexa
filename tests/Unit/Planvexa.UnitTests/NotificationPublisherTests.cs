namespace Planvexa.UnitTests.Notifications;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Notifications.Application;
using Planvexa.Modules.Notifications.Domain;
using Planvexa.SharedContracts.Notifications;
using Shouldly;
using Xunit;

/// <summary>
/// Realtime gap fix: <see cref="NotificationPublisher.PublishAsync"/> must also push a "Notification"
/// RealtimeEvent (keyed by recipient, see useRealtime.ts's per-user filter) so NotificationBell updates
/// without waiting for its 30s poll. See CommentService for the established NotifyAsync-per-mutation
/// pattern this mirrors.
/// </summary>
public sealed class NotificationPublisherTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid RecipientId = Guid.NewGuid();

    private static (NotificationPublisher Publisher, FakeRealtimeNotifier Realtime) BuildPublisher(
        FakeNotificationStore? store = null, FakeNotificationPreferenceStore? preferences = null)
    {
        var accessor = new WorkspaceContextAccessor();
        accessor.Set(new WorkspaceContext(WorkspaceId, Guid.NewGuid(), null, "Member", new HashSet<string>(), new HashSet<string>(), "corr-1"));
        var realtime = new FakeRealtimeNotifier();
        var publisher = new NotificationPublisher(
            accessor, store ?? new FakeNotificationStore(), preferences ?? new FakeNotificationPreferenceStore(),
            new FakeIdGenerator(), new FakeClock(), new FakeUnitOfWork(), realtime);
        return (publisher, realtime);
    }

    [Fact]
    public async Task Publishing_a_notification_broadcasts_a_realtime_event_keyed_by_recipient()
    {
        var (publisher, realtime) = BuildPublisher();

        await publisher.PublishAsync(new NotificationRequest(RecipientId, "mention", "Task", Guid.NewGuid(), WorkspaceId, "dedup-1"));

        realtime.Events.Count.ShouldBe(1);
        var evt = realtime.Events[0];
        evt.WorkspaceId.ShouldBe(WorkspaceId);
        evt.EntityType.ShouldBe("Notification");
        evt.EntityId.ShouldBe(RecipientId);
        evt.Action.ShouldBe("created");
    }

    [Fact]
    public async Task Deduplicated_notification_does_not_broadcast()
    {
        var store = new FakeNotificationStore { AlwaysExists = true };
        var (publisher, realtime) = BuildPublisher(store: store);

        await publisher.PublishAsync(new NotificationRequest(RecipientId, "mention", "Task", Guid.NewGuid(), WorkspaceId, "dedup-1"));

        realtime.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Opted_out_recipient_does_not_broadcast()
    {
        var preferences = new FakeNotificationPreferenceStore(
            NotificationPreference.Create(Guid.NewGuid(), WorkspaceId, RecipientId, "mention", inbox: false, email: false, push: false));
        var (publisher, realtime) = BuildPublisher(preferences: preferences);

        await publisher.PublishAsync(new NotificationRequest(RecipientId, "mention", "Task", Guid.NewGuid(), WorkspaceId, "dedup-1"));

        realtime.Events.ShouldBeEmpty();
    }

    private sealed class FakeNotificationStore : INotificationStore
    {
        public bool AlwaysExists { get; init; }

        public void Add(Notification notification)
        {
        }

        public Task<Notification?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default) => Task.FromResult<Notification?>(null);

        public Task<bool> ExistsAsync(Guid workspaceId, Guid recipientUserId, string deduplicationKey, CancellationToken ct = default)
            => Task.FromResult(AlwaysExists);

        public Task<IReadOnlyList<Notification>> ListForRecipientAsync(Guid workspaceId, Guid recipientUserId, bool unreadOnly, int max, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Notification>>([]);

        public Task<int> UnreadCountAsync(Guid workspaceId, Guid recipientUserId, CancellationToken ct = default) => Task.FromResult(0);

        public Task<IReadOnlyList<Notification>> ListUnreadForMarkAllAsync(Guid workspaceId, Guid recipientUserId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Notification>>([]);
    }

    private sealed class FakeNotificationPreferenceStore(NotificationPreference? preference = null) : INotificationPreferenceStore
    {
        public void Add(NotificationPreference preference)
        {
        }

        public Task<NotificationPreference?> FindAsync(Guid workspaceId, Guid userId, string eventType, CancellationToken ct = default)
            => Task.FromResult(preference);

        public Task<IReadOnlyList<NotificationPreference>> ListForUserAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NotificationPreference>>([]);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class FakeRealtimeNotifier : IRealtimeNotifier
    {
        public List<RealtimeEvent> Events { get; } = [];

        public Task NotifyAsync(RealtimeEvent realtimeEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(realtimeEvent);
            return Task.CompletedTask;
        }
    }
}
