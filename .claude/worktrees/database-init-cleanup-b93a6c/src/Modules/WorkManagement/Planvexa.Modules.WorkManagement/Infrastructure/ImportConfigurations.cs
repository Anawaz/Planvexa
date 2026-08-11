namespace Planvexa.Modules.WorkManagement.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.WorkManagement.Domain;

public sealed class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> b)
    {
        b.ToTable("import_jobs", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.SourceType).HasMaxLength(32).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        b.Property(x => x.ColumnMappingJson).HasColumnType("jsonb");
        b.Property(x => x.DetectedColumnsCsv);
        b.Property(x => x.TargetSpaceName).HasMaxLength(200);
        b.Property(x => x.TargetListName).HasMaxLength(200);
        b.HasIndex(x => new { x.WorkspaceId, x.Status });
        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.DetectedColumns);
    }
}

public sealed class ImportJobRowConfiguration : IEntityTypeConfiguration<ImportJobRow>
{
    public void Configure(EntityTypeBuilder<ImportJobRow> b)
    {
        b.ToTable("import_job_rows", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        b.Property(x => x.RawFieldsJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        b.Property(x => x.ErrorMessage).HasMaxLength(1000);
        b.HasIndex(x => new { x.ImportJobId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.ImportJobId, x.Status });
        // Explicit relationship (not just a plain Guid column) so EF's SaveChanges dependency graph
        // knows to insert the parent ImportJob before its rows when both are added in the same
        // SaveChangesAsync call (UploadAsync) — same pattern as TaskListMembership -> WorkItem.
        b.HasOne<ImportJob>().WithMany().HasForeignKey(x => x.ImportJobId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.DomainEvents);
    }
}
