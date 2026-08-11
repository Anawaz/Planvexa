namespace Planvexa.Modules.Identity.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Identity.Domain;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", IdentityModule.Schema);

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Subject).HasMaxLength(256).IsRequired();
        builder.HasIndex(u => u.Subject).IsUnique();

        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.CreatedAtUtc).IsRequired();
        builder.Property(u => u.IsAnonymized).IsRequired();

        builder.Ignore(u => u.DomainEvents);
    }
}
