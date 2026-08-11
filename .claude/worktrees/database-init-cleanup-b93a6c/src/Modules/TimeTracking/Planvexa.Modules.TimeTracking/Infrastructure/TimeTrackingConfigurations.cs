namespace Planvexa.Modules.TimeTracking.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.TimeTracking.Domain;

public sealed class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> b)
    {
        b.ToTable("time_entries", TimeTrackingModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.BillingRate).HasColumnType("numeric(18,4)");
        b.Property(x => x.CostRate).HasColumnType("numeric(18,4)");
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.ApprovalStatus).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.IdempotencyKey).HasMaxLength(200);

        b.HasIndex(x => new { x.WorkspaceId, x.UserId, x.StartedAtUtc });
        b.HasIndex(x => new { x.WorkspaceId, x.TaskId });
        b.HasIndex(x => new { x.WorkspaceId, x.StartedAtUtc });

        // Single active timer per user (ADR-0010): at most one running entry (ended_at_utc IS NULL).
        b.HasIndex(x => new { x.WorkspaceId, x.UserId })
            .IsUnique()
            .HasFilter("ended_at_utc IS NULL")
            .HasDatabaseName("ux_time_entries_single_active_timer");

        // Offline-mutation-outbox replay guard: unique per workspace when set (see IdempotencyKey's doc comment).
        b.HasIndex(x => new { x.WorkspaceId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("ux_time_entries_idempotency_key");

        b.HasMany(x => x.Tags).WithOne().HasForeignKey(t => t.TimeEntryId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TimeTagConfiguration : IEntityTypeConfiguration<TimeTag>
{
    public void Configure(EntityTypeBuilder<TimeTag> b)
    {
        b.ToTable("time_tags", TimeTrackingModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TimeEntryTagConfiguration : IEntityTypeConfiguration<TimeEntryTag>
{
    public void Configure(EntityTypeBuilder<TimeEntryTag> b)
    {
        b.ToTable("time_entry_tags", TimeTrackingModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TimeEntryId, x.TagId }).IsUnique();
        b.HasOne<TimeTag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> b)
    {
        b.ToTable("budgets", TimeTrackingModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.ScopeType).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.MonetaryCapAmount).HasColumnType("numeric(18,4)");
        b.HasIndex(x => new { x.WorkspaceId, x.ScopeType, x.ScopeId }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TimeEntryAuditConfiguration : IEntityTypeConfiguration<TimeEntryAudit>
{
    public void Configure(EntityTypeBuilder<TimeEntryAudit> b)
    {
        b.ToTable("time_entry_audits", TimeTrackingModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasMaxLength(64).IsRequired();
        b.Property(x => x.Detail).HasMaxLength(512);
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.HasIndex(x => new { x.WorkspaceId, x.TimeEntryId, x.CreatedAtUtc });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TimePolicyConfiguration : IEntityTypeConfiguration<TimePolicy>
{
    public void Configure(EntityTypeBuilder<TimePolicy> b)
    {
        b.ToTable("time_policies", TimeTrackingModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.WorkspaceId).IsUnique();
        b.Property(x => x.MissingTimeReminderCadence).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class MemberRateConfiguration : IEntityTypeConfiguration<MemberRate>
{
    public void Configure(EntityTypeBuilder<MemberRate> b)
    {
        b.ToTable("member_rates", TimeTrackingModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.BillingRate).HasColumnType("numeric(18,4)");
        b.Property(x => x.CostRate).HasColumnType("numeric(18,4)");
        b.HasIndex(x => new { x.WorkspaceId, x.UserId, x.ProjectId }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TimesheetPeriodConfiguration : IEntityTypeConfiguration<TimesheetPeriod>
{
    public void Configure(EntityTypeBuilder<TimesheetPeriod> b)
    {
        b.ToTable("timesheet_periods", TimeTrackingModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Cadence).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.UserId, x.PeriodStartUtc }).IsUnique();

        b.HasMany(x => x.Approvals).WithOne().HasForeignKey(a => a.PeriodId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Approvals).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TimesheetApprovalConfiguration : IEntityTypeConfiguration<TimesheetApproval>
{
    public void Configure(EntityTypeBuilder<TimesheetApproval> b)
    {
        b.ToTable("timesheet_approvals", TimeTrackingModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Comment).HasMaxLength(2000);
        b.HasIndex(x => x.PeriodId);
        b.Ignore(x => x.DomainEvents);
    }
}
