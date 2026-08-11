namespace Planvexa.Modules.Notifications.Application;

using System.Text.Json;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.Modules.Notifications.Domain;
using Planvexa.SharedContracts.Mobile;
using Planvexa.SharedContracts.Notifications;
using Planvexa.SharedContracts.Users;

/// <summary>
/// Drains pending notification deliveries and dispatches them through their channel. Email uses the
/// <see cref="IEmailSender"/> abstraction, Push uses <see cref="IPushSender"/> gated by
/// <see cref="IPushDeviceDirectory"/> (a Push delivery for a user with no registered device is
/// deterministically not eligible, so it is marked Suppressed rather than retried). Idempotent: only
/// Pending deliveries are processed, and each is marked Sent/Suppressed/Failed so retries never
/// double-send a Sent delivery. Runs with no ambient workspace.
/// </summary>
public sealed class NotificationDeliveryProcessor(
    INotificationDeliveryStore deliveries,
    IEmailSender emailSender,
    IPushSender pushSender,
    IPushDeviceDirectory pushDevices,
    IUserDirectory users,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Processes up to <paramref name="max"/> pending deliveries. Returns the number sent.</summary>
    public async Task<int> ProcessPendingAsync(int max, CancellationToken ct = default)
    {
        var pending = await deliveries.ListPendingAsync(max, ct);
        var sent = 0;

        foreach (var delivery in pending)
        {
            try
            {
                if (delivery.Channel == NotificationChannels.Email)
                {
                    var notification = await deliveries.FindByDeliveryAsync(delivery.NotificationId, ct);
                    if (notification is null)
                    {
                        delivery.MarkFailed("Owning notification not found.");
                        continue;
                    }

                    var recipient = await users.FindByIdAsync(notification.RecipientUserId, ct);
                    if (recipient is null)
                    {
                        delivery.MarkFailed("Recipient user not found.");
                        continue;
                    }

                    var (subject, body) = Render(notification);
                    await emailSender.SendAsync(notification.RecipientUserId, subject, body, ct);
                }
                else if (delivery.Channel == NotificationChannels.Push)
                {
                    var notification = await deliveries.FindByDeliveryAsync(delivery.NotificationId, ct);
                    if (notification is null)
                    {
                        delivery.MarkFailed("Owning notification not found.");
                        continue;
                    }

                    // Deterministic ineligibility (no device registered for this workspace) is not a
                    // transient failure — suppress it once rather than retrying up to 5 times.
                    if (!await pushDevices.HasActiveDeviceAsync(notification.WorkspaceId, notification.RecipientUserId, ct))
                    {
                        delivery.MarkSuppressed("No registered device for this user.");
                        continue;
                    }

                    var (subject, body) = Render(notification);
                    await pushSender.SendAsync(notification.WorkspaceId, notification.RecipientUserId, subject, body, ct);
                }

                delivery.MarkSent(clock.UtcNow);
                sent++;
            }
            catch (Exception ex)
            {
                delivery.MarkFailed(ex.Message);
            }
        }

        if (pending.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        return sent;
    }

    private static (string Subject, string Body) Render(Notification notification)
    {
        var payload = notification.Payload is null
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(notification.Payload, JsonOptions) ?? new();

        var taskTitle = payload.TryGetValue("taskTitle", out var title) ? title : "a task";
        var subject = notification.EventType switch
        {
            "mention" => $"You were mentioned on \"{taskTitle}\"",
            _ => $"New notification: {notification.EventType}",
        };
        var body = $"Event: {notification.EventType}\nEntity: {notification.EntityType} {notification.EntityId}\nTask: {taskTitle}";
        return (subject, body);
    }
}
