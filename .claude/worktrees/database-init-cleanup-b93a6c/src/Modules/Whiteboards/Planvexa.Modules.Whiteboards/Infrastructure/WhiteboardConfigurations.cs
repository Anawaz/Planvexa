namespace Planvexa.Modules.Whiteboards.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Whiteboards.Domain;

public sealed class WhiteboardConfiguration : IEntityTypeConfiguration<Whiteboard>
{
    public void Configure(EntityTypeBuilder<Whiteboard> b)
    {
        b.ToTable("whiteboards", WhiteboardsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.LinkedResourceType).HasMaxLength(32);
        b.HasIndex(x => x.WorkspaceId);
        b.HasIndex(x => new { x.LinkedResourceType, x.LinkedResourceId });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class WhiteboardTemplateConfiguration : IEntityTypeConfiguration<WhiteboardTemplate>
{
    public void Configure(EntityTypeBuilder<WhiteboardTemplate> b)
    {
        b.ToTable("whiteboard_templates", WhiteboardsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.SeedState).HasColumnName("seed_state");
        b.HasIndex(x => x.WorkspaceId);
        b.Ignore(x => x.DomainEvents);
    }
}
