namespace Planvexa.Modules.Governance.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Governance.Domain;

public sealed class EnterpriseSecuritySettingsConfiguration : IEntityTypeConfiguration<EnterpriseSecuritySettings>
{
    public void Configure(EntityTypeBuilder<EnterpriseSecuritySettings> b)
    {
        b.ToTable("enterprise_security_settings", GovernanceModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.SamlEntityId).HasMaxLength(256);
        b.Property(x => x.SamlMetadataUrl).HasMaxLength(2048);
        b.Property(x => x.ScimTokenHash).HasMaxLength(128);
        b.HasIndex(x => x.WorkspaceId).IsUnique();
        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.ScimTokenSet);
    }
}

public sealed class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
{
    public void Configure(EntityTypeBuilder<ExportJob> b)
    {
        b.ToTable("export_jobs", GovernanceModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Dataset).HasMaxLength(32).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Artifact).HasColumnType("text");
        b.Property(x => x.Error).HasMaxLength(2000);
        b.HasIndex(x => new { x.WorkspaceId, x.CreatedAtUtc });
        b.HasIndex(x => x.Status);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class WorkspaceIpAllowRuleConfiguration : IEntityTypeConfiguration<WorkspaceIpAllowRule>
{
    public void Configure(EntityTypeBuilder<WorkspaceIpAllowRule> b)
    {
        b.ToTable("workspace_ip_allow_rules", GovernanceModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Cidr).HasMaxLength(64).IsRequired();
        b.Property(x => x.Description).HasMaxLength(200);
        b.HasIndex(x => x.WorkspaceId);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class RetentionPolicyConfiguration : IEntityTypeConfiguration<RetentionPolicy>
{
    public void Configure(EntityTypeBuilder<RetentionPolicy> b)
    {
        b.ToTable("retention_policies", GovernanceModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.WorkspaceId).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

