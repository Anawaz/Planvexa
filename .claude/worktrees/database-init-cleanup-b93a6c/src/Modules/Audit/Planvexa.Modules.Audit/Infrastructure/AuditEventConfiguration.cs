namespace Planvexa.Modules.Audit.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Audit.Domain;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events", AuditModule.Schema);

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action).HasMaxLength(128).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(128).IsRequired();
        builder.Property(a => a.Data).HasColumnType("jsonb");
        builder.Property(a => a.CorrelationId).HasMaxLength(128);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.CreatedAtUtc).IsRequired();

        // Query patterns: by workspace + time, and by entity.
        builder.HasIndex(a => new { a.WorkspaceId, a.CreatedAtUtc });
        builder.HasIndex(a => new { a.WorkspaceId, a.EntityType, a.EntityId });
    }
}
