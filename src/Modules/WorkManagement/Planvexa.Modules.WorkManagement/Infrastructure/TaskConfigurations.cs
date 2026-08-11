namespace Planvexa.Modules.WorkManagement.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.WorkManagement.Domain;

public sealed class WorkItemConfiguration : IEntityTypeConfiguration<WorkItem>
{
    public void Configure(EntityTypeBuilder<WorkItem> b)
    {
        b.ToTable("tasks", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(500).IsRequired();
        b.Property(x => x.Priority).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Sequence).IsRequired();
        b.Property(x => x.CustomId).HasMaxLength(64);
        b.Property(x => x.IdempotencyKey).HasMaxLength(200);

        // Stored as ProseMirror/Lexical-shaped JSON; the domain/service layer still sees plain
        // text (see DescriptionJson's doc comment).
        b.Property(x => x.Description)
            .HasColumnType("jsonb")
            .HasConversion(v => DescriptionJson.ToJson(v), v => DescriptionJson.FromText(v));

        // Hot-path indexes.
        b.HasIndex(x => new { x.ListId, x.Position });
        b.HasIndex(x => new { x.ListId, x.StatusId });
        b.HasIndex(x => x.WorkspaceId);
        b.HasIndex(x => x.ParentId);
        b.HasIndex(x => x.TaskTypeId);

        // Custom id is unique per List (not workspace-wide) when set; NULLs are unconstrained.
        b.HasIndex(x => new { x.ListId, x.CustomId }).IsUnique().HasFilter("custom_id IS NOT NULL");

        // Offline-mutation-outbox replay guard: unique per workspace when set (see IdempotencyKey's doc comment).
        b.HasIndex(x => new { x.WorkspaceId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");

        b.HasOne<TaskList>().WithMany().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);


        b.HasMany(x => x.Assignees).WithOne().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Watchers).WithOne().HasForeignKey(w => w.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Tags).WithOne().HasForeignKey(t => t.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.TeamAssignees).WithOne().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Assignees).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Watchers).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.TeamAssignees).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TaskListMembershipConfiguration : IEntityTypeConfiguration<TaskListMembership>
{
    public void Configure(EntityTypeBuilder<TaskListMembership> b)
    {
        b.ToTable("task_list_memberships", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TaskId, x.ListId }).IsUnique();
        b.HasIndex(x => new { x.ListId, x.Position });
        b.HasOne<TaskList>().WithMany().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<WorkItem>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TaskTeamAssigneeConfiguration : IEntityTypeConfiguration<TaskTeamAssignee>
{
    public void Configure(EntityTypeBuilder<TaskTeamAssignee> b)
    {
        b.ToTable("task_team_assignees", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TaskId, x.TeamId }).IsUnique();
        b.HasIndex(x => x.TeamId);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TaskTypeConfiguration : IEntityTypeConfiguration<TaskType>
{
    public void Configure(EntityTypeBuilder<TaskType> b)
    {
        b.ToTable("task_types", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Color).HasMaxLength(32).IsRequired();
        b.Property(x => x.Icon).HasMaxLength(64);
        b.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TaskRelationConfiguration : IEntityTypeConfiguration<TaskRelation>
{
    public void Configure(EntityTypeBuilder<TaskRelation> b)
    {
        b.ToTable("task_relations", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TaskId, x.RelatedTaskId }).IsUnique();
        b.HasIndex(x => x.RelatedTaskId);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TaskAssigneeConfiguration : IEntityTypeConfiguration<TaskAssignee>
{
    public void Configure(EntityTypeBuilder<TaskAssignee> b)
    {
        b.ToTable("task_assignees", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TaskId, x.UserId }).IsUnique();
        b.HasIndex(x => x.UserId);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TaskWatcherConfiguration : IEntityTypeConfiguration<TaskWatcher>
{
    public void Configure(EntityTypeBuilder<TaskWatcher> b)
    {
        b.ToTable("task_watchers", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TaskId, x.UserId }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.ToTable("tags", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Color).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TaskTagConfiguration : IEntityTypeConfiguration<TaskTag>
{
    public void Configure(EntityTypeBuilder<TaskTag> b)
    {
        b.ToTable("task_tags", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TaskId, x.TagId }).IsUnique();
        b.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> b)
    {
        b.ToTable("task_dependencies", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.HasIndex(x => new { x.TaskId, x.DependsOnTaskId, x.Type }).IsUnique();
        b.HasIndex(x => x.DependsOnTaskId);
        b.Ignore(x => x.DomainEvents);
    }
}
