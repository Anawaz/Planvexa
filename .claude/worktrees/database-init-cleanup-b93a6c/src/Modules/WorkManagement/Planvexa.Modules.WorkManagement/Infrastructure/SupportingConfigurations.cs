namespace Planvexa.Modules.WorkManagement.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.WorkManagement.Domain;

public sealed class TaskChecklistConfiguration : IEntityTypeConfiguration<TaskChecklist>
{
    public void Configure(EntityTypeBuilder<TaskChecklist> b)
    {
        b.ToTable("task_checklists", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.TaskId);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.ChecklistId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TaskChecklistItemConfiguration : IEntityTypeConfiguration<TaskChecklistItem>
{
    public void Configure(EntityTypeBuilder<TaskChecklistItem> b)
    {
        b.ToTable("task_checklist_items", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Content).HasMaxLength(1000).IsRequired();
        b.HasIndex(x => x.ChecklistId);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TaskAttachmentConfiguration : IEntityTypeConfiguration<TaskAttachment>
{
    public void Configure(EntityTypeBuilder<TaskAttachment> b)
    {
        b.ToTable("task_attachments", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(200).IsRequired();
        b.Property(x => x.StoragePath).HasMaxLength(500).IsRequired();
        b.HasIndex(x => x.TaskId);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> b)
    {
        b.ToTable("custom_field_definitions", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Scope).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(24).IsRequired();

        // Formula/rollup definition columns.
        b.Property(x => x.FormulaExpression).HasMaxLength(2000);
        b.Property(x => x.FormulaDependencyIdsCsv).HasColumnName("formula_dependency_ids").HasMaxLength(4000);
        b.Property(x => x.RollupSourceType).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.RollupFunction).HasConversion<string>().HasMaxLength(16);

        b.HasIndex(x => x.WorkspaceId);
        b.HasMany(x => x.Options).WithOne().HasForeignKey(o => o.DefinitionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class CustomFieldOptionConfiguration : IEntityTypeConfiguration<CustomFieldOption>
{
    public void Configure(EntityTypeBuilder<CustomFieldOption> b)
    {
        b.ToTable("custom_field_options", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Label).HasMaxLength(200).IsRequired();
        b.Property(x => x.Color).HasMaxLength(32);
        b.HasIndex(x => x.DefinitionId);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class CustomFieldValueConfiguration : IEntityTypeConfiguration<CustomFieldValue>
{
    public void Configure(EntityTypeBuilder<CustomFieldValue> b)
    {
        b.ToTable("custom_field_values", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.TextValue).HasMaxLength(4000);
        b.Property(x => x.NumberValue).HasColumnType("numeric");
        b.Property(x => x.JsonValue).HasColumnType("jsonb");

        // One value per (task, definition); typed columns are indexed for filtering/sorting (ADR-0008).
        b.HasIndex(x => new { x.TaskId, x.DefinitionId }).IsUnique();
        b.HasIndex(x => new { x.DefinitionId, x.NumberValue });
        b.HasIndex(x => new { x.DefinitionId, x.DateValue });
        b.HasIndex(x => new { x.DefinitionId, x.OptionId });

        b.HasOne<CustomFieldDefinition>().WithMany().HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class CustomFieldRelationshipValueConfiguration : IEntityTypeConfiguration<CustomFieldRelationshipValue>
{
    public void Configure(EntityTypeBuilder<CustomFieldRelationshipValue> b)
    {
        b.ToTable("custom_field_relationship_values", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.DefinitionId, x.TaskId, x.RelatedTaskId }).IsUnique();
        b.HasIndex(x => x.RelatedTaskId);
        b.HasOne<CustomFieldDefinition>().WithMany().HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class RecurringTaskDefinitionConfiguration : IEntityTypeConfiguration<RecurringTaskDefinition>
{
    public void Configure(EntityTypeBuilder<RecurringTaskDefinition> b)
    {
        b.ToTable("recurring_task_definitions", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(500).IsRequired();
        b.Property(x => x.Priority).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Frequency).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.ListId);
        b.HasIndex(x => new { x.IsActive, x.NextRunUtc });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class RecurringOccurrenceConfiguration : IEntityTypeConfiguration<RecurringOccurrence>
{
    public void Configure(EntityTypeBuilder<RecurringOccurrence> b)
    {
        b.ToTable("recurring_occurrences", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.OccurrenceKey).HasMaxLength(128).IsRequired();

        // The uniqueness that makes generation idempotent (ADR-0009).
        b.HasIndex(x => new { x.DefinitionId, x.OccurrenceKey }).IsUnique();

        b.HasOne<RecurringTaskDefinition>().WithMany().HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TaskActivityEventConfiguration : IEntityTypeConfiguration<TaskActivityEvent>
{
    public void Configure(EntityTypeBuilder<TaskActivityEvent> b)
    {
        b.ToTable("task_activity_events", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasMaxLength(64).IsRequired();
        b.Property(x => x.Data).HasMaxLength(2000);
        b.HasIndex(x => new { x.TaskId, x.CreatedAtUtc });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class SavedViewConfiguration : IEntityTypeConfiguration<SavedView>
{
    public void Configure(EntityTypeBuilder<SavedView> b)
    {
        b.ToTable("saved_views", WorkManagementModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.ViewType).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.ScopeType).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.ConfigJson).HasColumnType("jsonb").IsRequired();
        b.HasIndex(x => x.WorkspaceId);
        b.Ignore(x => x.DomainEvents);
    }
}
