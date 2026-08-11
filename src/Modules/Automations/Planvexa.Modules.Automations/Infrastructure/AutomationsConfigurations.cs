namespace Planvexa.Modules.Automations.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Automations.Domain;

public sealed class AutomationRuleConfiguration : IEntityTypeConfiguration<AutomationRule>
{
    public void Configure(EntityTypeBuilder<AutomationRule> b)
    {
        b.ToTable("automation_rules", AutomationsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.TriggerType).HasMaxLength(64).IsRequired();
        b.Property(x => x.ConditionJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.ActionJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.TriggerConfigJson).HasColumnType("jsonb");
        b.Property(x => x.Version).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.TriggerType, x.IsEnabled });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class AutomationRuleVersionConfiguration : IEntityTypeConfiguration<AutomationRuleVersion>
{
    public void Configure(EntityTypeBuilder<AutomationRuleVersion> b)
    {
        b.ToTable("automation_rule_versions", AutomationsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.TriggerType).HasMaxLength(64).IsRequired();
        b.Property(x => x.ConditionJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.ActionJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.TriggerConfigJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.RuleId, x.Version }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class AutomationRunConfiguration : IEntityTypeConfiguration<AutomationRun>
{
    public void Configure(EntityTypeBuilder<AutomationRun> b)
    {
        b.ToTable("automation_runs", AutomationsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Detail).HasMaxLength(1000);
        b.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
        b.Property(x => x.DataJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Attempts).IsRequired();
        b.HasIndex(x => new { x.RuleId, x.EventId }).IsUnique();
        b.HasIndex(x => new { x.WorkspaceId, x.OccurredAtUtc });
        // The retry sweep scans across every workspace for due retries — an index on the filter columns
        // keeps that cheap (mirrors 0061's ix_scheduled_reports_is_enabled partial-index pattern).
        b.HasIndex(x => new { x.Status, x.NextRetryAtUtc });
        b.Ignore(x => x.DomainEvents);
    }
}
