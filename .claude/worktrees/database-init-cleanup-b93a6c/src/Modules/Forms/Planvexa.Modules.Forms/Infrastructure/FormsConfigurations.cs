namespace Planvexa.Modules.Forms.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Forms.Domain;

public sealed class FormConfiguration : IEntityTypeConfiguration<Form>
{
    public void Configure(EntityTypeBuilder<Form> b)
    {
        b.ToTable("forms", FormsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.PublicToken).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.PublicToken).IsUnique();
        b.HasIndex(x => x.WorkspaceId);

        // Branding, confirmation page, spam threshold, submission limits, full routing.
        b.Property(x => x.BrandingLogoUrl).HasMaxLength(2000);
        b.Property(x => x.BrandingColor).HasMaxLength(16);
        b.Property(x => x.ConfirmationMessage).HasMaxLength(2000);
        b.Property(x => x.ConfirmationRedirectUrl).HasMaxLength(2000);
        b.Property(x => x.TargetStatusName).HasMaxLength(100);
        b.Property(x => x.TargetPriority).HasMaxLength(20);
        b.Property(x => x.TargetTagsCsv).HasMaxLength(1000);
        b.Ignore(x => x.TargetTags);

        b.HasMany(x => x.Fields).WithOne().HasForeignKey(f => f.FormId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Fields).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class FormFieldConfiguration : IEntityTypeConfiguration<FormField>
{
    public void Configure(EntityTypeBuilder<FormField> b)
    {
        b.ToTable("form_fields", FormsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Label).HasMaxLength(200).IsRequired();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.OptionsCsv).HasMaxLength(2000);
        b.Property(x => x.ConditionOperator).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ConditionValue).HasMaxLength(500);
        b.HasIndex(x => x.FormId);
        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.Options);
    }
}

public sealed class FormSubmissionConfiguration : IEntityTypeConfiguration<FormSubmission>
{
    public void Configure(EntityTypeBuilder<FormSubmission> b)
    {
        b.ToTable("form_submissions", FormsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.ValuesJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        b.Property(x => x.RespondentKey).HasMaxLength(128);
        b.HasIndex(x => new { x.FormId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.FormId, x.SubmittedAtUtc });
        b.HasIndex(x => new { x.FormId, x.RespondentKey });
        b.Ignore(x => x.DomainEvents);
    }
}

/// <summary>Metadata for a pending file upload awaiting its submission.</summary>
public sealed class FormUploadConfiguration : IEntityTypeConfiguration<FormUpload>
{
    public void Configure(EntityTypeBuilder<FormUpload> b)
    {
        b.ToTable("form_uploads", FormsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.StoragePath).HasMaxLength(1000).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(300).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.FormId);
        b.Ignore(x => x.DomainEvents);
    }
}
