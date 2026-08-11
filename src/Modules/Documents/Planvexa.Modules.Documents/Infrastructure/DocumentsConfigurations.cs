namespace Planvexa.Modules.Documents.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Documents.Domain;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> b)
    {
        b.ToTable("documents", DocumentsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        // Stays a plain text column holding a serialized Lexical editor-state JSON string (see
        // LexicalJson's doc comment) — not jsonb, since the app never queries into it and a value
        // converter would just re-encode a string that is already JSON text.
        b.Property(x => x.Content).HasColumnType("text");
        b.HasIndex(x => x.WorkspaceId);
        b.HasIndex(x => x.ParentDocumentId);

        // Self-referencing wiki tree. Restrict, not cascade: deleting a document with children must
        // fail loudly (DocumentService re-parents/blocks first) rather than silently taking a subtree with
        // it — mirrors Folder's parent FK behaviour.
        b.HasOne<Document>().WithMany().HasForeignKey(x => x.ParentDocumentId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.DocumentId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class DocumentTemplateConfiguration : IEntityTypeConfiguration<DocumentTemplate>
{
    public void Configure(EntityTypeBuilder<DocumentTemplate> b)
    {
        b.ToTable("document_templates", DocumentsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.ContentJson).HasColumnName("content").HasColumnType("text");
        b.HasIndex(x => x.WorkspaceId);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> b)
    {
        b.ToTable("document_versions", DocumentsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Content).HasColumnType("text");
        b.HasIndex(x => new { x.DocumentId, x.CreatedAtUtc });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class DocumentCommentConfiguration : IEntityTypeConfiguration<DocumentComment>
{
    public void Configure(EntityTypeBuilder<DocumentComment> b)
    {
        b.ToTable("document_comments", DocumentsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.DocumentId, x.CreatedAtUtc });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class DocumentShareLinkConfiguration : IEntityTypeConfiguration<DocumentShareLink>
{
    public void Configure(EntityTypeBuilder<DocumentShareLink> b)
    {
        b.ToTable("document_share_links", DocumentsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(256);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.WorkspaceId, x.DocumentId });
        b.Ignore(x => x.DomainEvents);
    }
}
