namespace Planvexa.Modules.Mobile.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Mobile.Domain;

public sealed class DeviceRegistrationConfiguration : IEntityTypeConfiguration<DeviceRegistration>
{
    public void Configure(EntityTypeBuilder<DeviceRegistration> b)
    {
        b.ToTable("device_registrations", MobileModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Platform).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.AppVersion).HasMaxLength(64);
        b.Property(x => x.PushEndpoint).HasMaxLength(2048);
        b.Property(x => x.PushP256dh).HasMaxLength(256);
        b.Property(x => x.PushAuth).HasMaxLength(256);
        b.HasIndex(x => new { x.WorkspaceId, x.UserId, x.TokenHash }).IsUnique();
        b.HasIndex(x => new { x.WorkspaceId, x.UserId });
        b.Ignore(x => x.DomainEvents);
    }
}
