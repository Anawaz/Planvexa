namespace Planvexa.Modules.Notifications.Application;

using System.Text.Json;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Notifications.Domain;

public sealed record NotificationDto(
    Guid Id, string EventType, string EntityType, Guid EntityId, Guid WorkspaceId,
    IReadOnlyDictionary<string, string>? Payload, DateTimeOffset CreatedAtUtc, DateTimeOffset? ReadAtUtc);

public sealed record PreferenceDto(string EventType, bool Inbox, bool Email, bool Push);

public sealed record DigestPreferenceDto(string Frequency, DateTimeOffset? LastSentAtUtc);

/// <summary>Reads and mutates the current user's notification inbox and preferences.</summary>
public sealed class NotificationInboxService(
    IWorkspaceContextAccessor workspaceAccessor,
    ICurrentUser currentUser,
    INotificationStore notifications,
    INotificationPreferenceStore preferences,
    IDigestPreferenceStore digestPreferences,
    IIdGenerator ids,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<NotificationDto>> ListAsync(bool unreadOnly, int max, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        var list = await notifications.ListForRecipientAsync(workspaceId, currentUser.UserId, unreadOnly, max, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        return await notifications.UnreadCountAsync(workspaceId, currentUser.UserId, ct);
    }

    public async Task MarkReadAsync(Guid notificationId, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        var notification = await notifications.FindAsync(workspaceId, notificationId, ct)
            ?? throw new NotFoundException("Notification not found.");
        if (notification.RecipientUserId != currentUser.UserId)
        {
            throw new ForbiddenException("You can only read your own notifications.");
        }

        notification.MarkRead(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        var now = clock.UtcNow;
        var unread = await notifications.ListUnreadForMarkAllAsync(workspaceId, currentUser.UserId, ct);
        foreach (var notification in unread)
        {
            notification.MarkRead(now);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PreferenceDto>> ListPreferencesAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        var list = await preferences.ListForUserAsync(workspaceId, currentUser.UserId, ct);
        return list.Select(p => new PreferenceDto(p.EventType, p.Inbox, p.Email, p.Push)).ToList();
    }

    public async Task<PreferenceDto> SetPreferenceAsync(string eventType, bool inbox, bool email, bool push, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        var existing = await preferences.FindAsync(workspaceId, currentUser.UserId, eventType, ct);
        if (existing is null)
        {
            existing = NotificationPreference.Create(ids.NewId(), workspaceId, currentUser.UserId, eventType, inbox, email, push);
            preferences.Add(existing);
        }
        else
        {
            existing.Update(inbox, email, push);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return new PreferenceDto(existing.EventType, existing.Inbox, existing.Email, existing.Push);
    }

    public async Task<DigestPreferenceDto> GetDigestPreferenceAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        var existing = await digestPreferences.FindAsync(workspaceId, currentUser.UserId, ct);
        return new DigestPreferenceDto((existing?.Frequency ?? DigestFrequency.Off).ToString(), existing?.LastSentAtUtc);
    }

    public async Task<DigestPreferenceDto> SetDigestPreferenceAsync(string frequency, CancellationToken ct = default)
    {
        if (!Enum.TryParse<DigestFrequency>(frequency, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new ValidationAppException("Unsupported digest frequency. Use Off, Daily or Weekly.");
        }

        var workspaceId = RequireWorkspace();
        var existing = await digestPreferences.FindAsync(workspaceId, currentUser.UserId, ct);
        if (existing is null)
        {
            existing = DigestPreference.Create(ids.NewId(), workspaceId, currentUser.UserId, parsed, clock.UtcNow);
            digestPreferences.Add(existing);
        }
        else
        {
            existing.SetFrequency(parsed);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return new DigestPreferenceDto(existing.Frequency.ToString(), existing.LastSentAtUtc);
    }

    private static NotificationDto ToDto(Notification n)
    {
        var payload = n.Payload is null
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(n.Payload, JsonOptions);
        return new NotificationDto(n.Id, n.EventType, n.EntityType, n.EntityId, n.WorkspaceId, payload, n.CreatedAtUtc, n.ReadAtUtc);
    }

    private Guid RequireWorkspace()
    {
        var workspace = workspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            throw new ForbiddenException("A workspace context is required for this operation.");
        }

        return workspace.WorkspaceId;
    }
}
