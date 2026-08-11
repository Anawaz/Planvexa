namespace Planvexa.Modules.Notifications.Application;

using Planvexa.SharedContracts.Notifications;

/// <summary>
/// Resolves which channels a notification should use for a given user + event type. Defaults: the
/// inbox is always on; email is on unless the user has turned it off. Explicit preferences override.
/// </summary>
public static class NotificationPolicy
{
    // Push defaults to off: it requires an explicit opt-in (and a registered device) unlike inbox/email.
    public static NotificationChannels DefaultChannels(string eventType)
        => NotificationChannels.Inbox | NotificationChannels.Email;

    public static NotificationChannels Resolve(string eventType, Domain.NotificationPreference? preference)
    {
        if (preference is null)
        {
            return DefaultChannels(eventType);
        }

        var channels = NotificationChannels.None;
        if (preference.Inbox)
        {
            channels |= NotificationChannels.Inbox;
        }

        if (preference.Email)
        {
            channels |= NotificationChannels.Email;
        }

        if (preference.Push)
        {
            channels |= NotificationChannels.Push;
        }

        return channels;
    }
}
