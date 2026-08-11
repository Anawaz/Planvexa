namespace Planvexa.Modules.Collaboration.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Collaboration.Domain;

public sealed class PublicShareLinkConfiguration : IEntityTypeConfiguration<PublicShareLink>
{
    /// <summary>Share links live in their own <c>sharing</c> schema (owned by the Collaboration module).</summary>
    public const string Schema = "sharing";

    public void Configure(EntityTypeBuilder<PublicShareLink> b)
    {
        b.ToTable("share_links", Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(256);
        b.Property(x => x.Level).HasColumnName("permission_level").HasConversion<int>();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.WorkspaceId, x.TaskId });
        b.Ignore(x => x.DomainEvents);
    }
}
