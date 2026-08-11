namespace Planvexa.Modules.Notifications.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Notifications.Domain;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications", NotificationsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
        b.Property(x => x.Payload).HasColumnType("jsonb");
        b.Property(x => x.DeduplicationKey).HasMaxLength(200).IsRequired();

        b.HasIndex(x => new { x.WorkspaceId, x.RecipientUserId, x.ReadAtUtc });
        b.HasIndex(x => new { x.WorkspaceId, x.RecipientUserId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.WorkspaceId, x.RecipientUserId, x.DeduplicationKey }).IsUnique();

        b.HasMany(x => x.Deliveries).WithOne().HasForeignKey(d => d.NotificationId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Deliveries).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> b)
    {
        b.ToTable("notification_deliveries", NotificationsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Error).HasMaxLength(2048);
        b.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> b)
    {
        b.ToTable("notification_preferences", NotificationsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.UserId, x.EventType }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class DigestPreferenceConfiguration : IEntityTypeConfiguration<DigestPreference>
{
    public void Configure(EntityTypeBuilder<DigestPreference> b)
    {
        b.ToTable("digest_preferences", NotificationsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Frequency).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.UserId }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}
