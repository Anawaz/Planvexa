namespace Planvexa.Modules.WorkManagement.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.WorkManagement.Domain;

public sealed class WorkTemplateConfiguration : IEntityTypeConfiguration<WorkTemplate>
{
    public void Configure(EntityTypeBuilder<WorkTemplate> b)
    {
        b.ToTable("work_templates", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.ResourceType).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.StructureJson).HasColumnType("jsonb").IsRequired();
        b.HasIndex(x => x.WorkspaceId);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class WorkFavoriteConfiguration : IEntityTypeConfiguration<WorkFavorite>
{
    public void Configure(EntityTypeBuilder<WorkFavorite> b)
    {
        b.ToTable("work_favorites", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.ResourceType).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.UserId, x.ResourceType, x.ResourceId }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class RecentItemConfiguration : IEntityTypeConfiguration<RecentItem>
{
    public void Configure(EntityTypeBuilder<RecentItem> b)
    {
        b.ToTable("recent_items", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.ResourceType).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.UserId, x.ResourceType, x.ResourceId }).IsUnique();
        b.HasIndex(x => new { x.WorkspaceId, x.UserId, x.ViewedAtUtc });
        b.Ignore(x => x.DomainEvents);
    }
}
