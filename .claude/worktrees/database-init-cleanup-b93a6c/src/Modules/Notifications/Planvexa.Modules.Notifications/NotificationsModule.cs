namespace Planvexa.Modules.Notifications;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Notifications.Application;
using Planvexa.SharedContracts.Notifications;

public static class NotificationsModule
{
    public const string Schema = "notifications";

    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddScoped<INotificationPublisher, NotificationPublisher>();
        services.AddScoped<NotificationInboxService>();
        services.AddScoped<NotificationDeliveryProcessor>();
        services.AddScoped<DigestRunner>();
        return services;
    }
}
