namespace Planvexa.Modules.Goals.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Goals.Domain;

public sealed class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> b)
    {
        b.ToTable("goals", GoalsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.TargetType).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(x => x.TargetValue).HasColumnType("numeric(18,4)");
        b.Property(x => x.CurrentValue).HasColumnType("numeric(18,4)");
        b.Property(x => x.Unit).HasConversion<string>().HasMaxLength(32).IsRequired();

        b.HasIndex(x => new { x.WorkspaceId, x.FolderId });

        b.HasMany(x => x.LinkedTasks).WithOne().HasForeignKey(t => t.GoalId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.LinkedTasks).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasMany(x => x.KeyResults).WithOne().HasForeignKey(k => k.GoalId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.KeyResults).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class GoalKeyResultConfiguration : IEntityTypeConfiguration<GoalKeyResult>
{
    public void Configure(EntityTypeBuilder<GoalKeyResult> b)
    {
        b.ToTable("goal_key_results", GoalsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.CurrentValue).HasColumnType("numeric(18,4)");
        b.Property(x => x.TargetValue).HasColumnType("numeric(18,4)");
        b.Property(x => x.Unit).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.GoalId });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class GoalLinkedTaskConfiguration : IEntityTypeConfiguration<GoalLinkedTask>
{
    public void Configure(EntityTypeBuilder<GoalLinkedTask> b)
    {
        b.ToTable("goal_linked_tasks", GoalsModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.GoalId, x.TaskId }).IsUnique();
        b.HasIndex(x => new { x.WorkspaceId, x.TaskId });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class GoalFolderConfiguration : IEntityTypeConfiguration<GoalFolder>
{
    public void Configure(EntityTypeBuilder<GoalFolder> b)
    {
        b.ToTable("goal_folders", GoalsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.WorkspaceId);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class GoalCommentConfiguration : IEntityTypeConfiguration<GoalComment>
{
    public void Configure(EntityTypeBuilder<GoalComment> b)
    {
        b.ToTable("goal_comments", GoalsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.GoalId, x.CreatedAtUtc });
        b.Ignore(x => x.DomainEvents);
    }
}
