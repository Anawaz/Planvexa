namespace Planvexa.Modules.Planning.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Planning.Domain;

public sealed class WorkScheduleConfiguration : IEntityTypeConfiguration<WorkSchedule>
{
    public void Configure(EntityTypeBuilder<WorkSchedule> b)
    {
        b.ToTable("work_schedules", PlanningModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.DailyCapacityHours).HasColumnType("numeric(6,2)");
        b.HasIndex(x => x.WorkspaceId).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> b)
    {
        b.ToTable("holidays", PlanningModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.DateUtc).HasColumnType("date");
        b.HasIndex(x => new { x.WorkspaceId, x.DateUtc });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class LeaveEntryConfiguration : IEntityTypeConfiguration<LeaveEntry>
{
    public void Configure(EntityTypeBuilder<LeaveEntry> b)
    {
        b.ToTable("leave_entries", PlanningModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.StartDate).HasColumnType("date");
        b.Property(x => x.EndDate).HasColumnType("date");
        b.HasIndex(x => new { x.WorkspaceId, x.UserId, x.StartDate });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TaskEstimateConfiguration : IEntityTypeConfiguration<TaskEstimate>
{
    public void Configure(EntityTypeBuilder<TaskEstimate> b)
    {
        b.ToTable("task_estimates", PlanningModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.WorkspaceId, x.TaskId }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class SprintConfiguration : IEntityTypeConfiguration<Sprint>
{
    public void Configure(EntityTypeBuilder<Sprint> b)
    {
        b.ToTable("sprints", PlanningModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.StartDate).HasColumnType("date");
        b.Property(x => x.EndDate).HasColumnType("date");
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Goal).HasMaxLength(2000);
        b.HasIndex(x => x.WorkspaceId);

        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.SprintId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class SprintItemConfiguration : IEntityTypeConfiguration<SprintItem>
{
    public void Configure(EntityTypeBuilder<SprintItem> b)
    {
        b.ToTable("sprint_items", PlanningModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.SprintId, x.TaskId }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}
