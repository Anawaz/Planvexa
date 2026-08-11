namespace Planvexa.Modules.Clips.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Clips.Domain;

public sealed class ClipConfiguration : IEntityTypeConfiguration<Clip>
{
    public void Configure(EntityTypeBuilder<Clip> b)
    {
        b.ToTable("clips", ClipsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.LinkedResourceType).HasMaxLength(32);
        b.Property(x => x.StoragePath).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        b.HasIndex(x => x.WorkspaceId);
        b.HasIndex(x => new { x.LinkedResourceType, x.LinkedResourceId });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ClipCommentConfiguration : IEntityTypeConfiguration<ClipComment>
{
    public void Configure(EntityTypeBuilder<ClipComment> b)
    {
        b.ToTable("clip_comments", ClipsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.ClipId, x.CreatedAtUtc });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ClipTranscriptConfiguration : IEntityTypeConfiguration<ClipTranscript>
{
    public void Configure(EntityTypeBuilder<ClipTranscript> b)
    {
        b.ToTable("clip_transcripts", ClipsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        b.Property(x => x.Text).HasColumnType("text");
        b.Property(x => x.SegmentsJson).HasColumnName("segments_json").HasColumnType("text");
        b.HasIndex(x => x.ClipId).IsUnique();
        b.HasIndex(x => x.WorkspaceId);
        b.Ignore(x => x.DomainEvents);
    }
}
