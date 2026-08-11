namespace Planvexa.Modules.WorkManagement.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.WorkManagement.Domain;

public sealed class SpaceConfiguration : IEntityTypeConfiguration<Space>
{
    public void Configure(EntityTypeBuilder<Space> b)
    {
        b.ToTable("spaces", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Color).HasMaxLength(32);
        b.Property(x => x.Icon).HasMaxLength(64);
        b.HasIndex(x => x.WorkspaceId);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> b)
    {
        b.ToTable("folders", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.SpaceId);
        b.HasIndex(x => x.ParentFolderId);
        b.HasOne<Space>().WithMany().HasForeignKey(x => x.SpaceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Folder>().WithMany().HasForeignKey(x => x.ParentFolderId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TaskListConfiguration : IEntityTypeConfiguration<TaskList>
{
    public void Configure(EntityTypeBuilder<TaskList> b)
    {
        b.ToTable("lists", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.SpaceId);
        b.HasOne<Space>().WithMany().HasForeignKey(x => x.SpaceId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class StatusSchemeConfiguration : IEntityTypeConfiguration<StatusScheme>
{
    public void Configure(EntityTypeBuilder<StatusScheme> b)
    {
        b.ToTable("status_schemes", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.WorkspaceId);
        b.HasMany(x => x.Statuses).WithOne().HasForeignKey(s => s.SchemeId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Statuses).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class StatusDefinitionConfiguration : IEntityTypeConfiguration<StatusDefinition>
{
    public void Configure(EntityTypeBuilder<StatusDefinition> b)
    {
        b.ToTable("statuses", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(128).IsRequired();
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(x => x.Color).HasMaxLength(32).IsRequired();
        b.HasIndex(x => x.SchemeId);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TaskReminderConfiguration : IEntityTypeConfiguration<TaskReminder>
{
    public void Configure(EntityTypeBuilder<TaskReminder> b)
    {
        b.ToTable("task_reminders", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => x.TaskId);
        b.HasIndex(x => new { x.IsSent, x.RemindAtUtc });
        b.Ignore(x => x.DomainEvents);
    }
}
