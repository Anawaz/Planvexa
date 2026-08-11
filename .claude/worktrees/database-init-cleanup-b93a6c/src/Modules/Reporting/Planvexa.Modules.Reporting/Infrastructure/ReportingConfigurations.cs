namespace Planvexa.Modules.Reporting.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Reporting.Domain;

public sealed class DashboardConfiguration : IEntityTypeConfiguration<Dashboard>
{
    public void Configure(EntityTypeBuilder<Dashboard> b)
    {
        b.ToTable("dashboards", ReportingModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.WorkspaceId);

        b.HasMany(x => x.Widgets).WithOne().HasForeignKey(w => w.DashboardId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Widgets).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class DashboardWidgetConfiguration : IEntityTypeConfiguration<DashboardWidget>
{
    public void Configure(EntityTypeBuilder<DashboardWidget> b)
    {
        b.ToTable("dashboard_widgets", ReportingModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(x => x.ConfigJson).HasColumnType("jsonb").IsRequired();
        b.HasIndex(x => x.DashboardId);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class RiskConfiguration : IEntityTypeConfiguration<Risk>
{
    public void Configure(EntityTypeBuilder<Risk> b)
    {
        b.ToTable("risks", ReportingModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.ScopeType).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.ScopeType, x.ScopeId });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ScheduledReportConfiguration : IEntityTypeConfiguration<ScheduledReport>
{
    public void Configure(EntityTypeBuilder<ScheduledReport> b)
    {
        b.ToTable("scheduled_reports", ReportingModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.RecipientsCsv).HasColumnName("recipients_csv").HasMaxLength(4000).IsRequired();
        b.Ignore(x => x.Recipients);
        b.Property(x => x.Cadence).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.DashboardId });
        b.Ignore(x => x.DomainEvents);
    }
}
