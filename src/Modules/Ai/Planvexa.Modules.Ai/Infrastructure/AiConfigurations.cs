namespace Planvexa.Modules.Ai.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Ai.Domain;

public sealed class AiRequestConfiguration : IEntityTypeConfiguration<AiRequest>
{
    public void Configure(EntityTypeBuilder<AiRequest> b)
    {
        b.ToTable("ai_requests", AiModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.RequestKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(24).IsRequired();
        b.Property(x => x.Result).HasColumnType("text").IsRequired();
        b.Property(x => x.RedactedTypes).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.RequestKey }).IsUnique();
        b.HasIndex(x => new { x.WorkspaceId, x.CreatedAtUtc });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class AiProviderSettingsConfiguration : IEntityTypeConfiguration<AiProviderSettings>
{
    public void Configure(EntityTypeBuilder<AiProviderSettings> b)
    {
        b.ToTable("provider_settings", AiModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.BaseUrl).HasMaxLength(500).IsRequired();
        b.Property(x => x.Model).HasMaxLength(200).IsRequired();
        b.Property(x => x.ApiKeyEncrypted).HasMaxLength(2000).IsRequired();
        b.Property(x => x.CreditLimit);
        b.Property(x => x.AllowedModelsJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.CustomRedactionPatternsJson).HasColumnType("jsonb").IsRequired();
        b.Ignore(x => x.IsUsable);
        b.Ignore(x => x.AllowedModels);
        b.Ignore(x => x.CustomRedactionPatterns);
        b.Ignore(x => x.RedactionOptions);
        b.HasIndex(x => x.WorkspaceId).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}
