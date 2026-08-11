namespace Planvexa.Modules.WorkManagement.Infrastructure;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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

/// <summary>Global per-user row — no WorkspaceId, no workspace query filter (see MyWorkPreference's doc
/// comment). One row per user, enforced by the unique index below. HiddenSections is opaque jsonb, same
/// convention as WorkTemplate.StructureJson/Task.Description above — only ever a small string array.</summary>
public sealed class MyWorkPreferenceConfiguration : IEntityTypeConfiguration<MyWorkPreference>
{
    public void Configure(EntityTypeBuilder<MyWorkPreference> b)
    {
        b.ToTable("my_work_preferences", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.SortBy).HasMaxLength(32).IsRequired();
        b.Property(x => x.HiddenSections)
            .HasColumnType("jsonb")
            .HasConversion(
                sections => JsonSerializer.Serialize(sections, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>(),
                new ValueComparer<IReadOnlyList<string>>(
                    (a, b2) => a!.SequenceEqual(b2!),
                    a => a.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                    a => a.ToList()))
            .IsRequired();
        b.HasIndex(x => x.UserId).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}
