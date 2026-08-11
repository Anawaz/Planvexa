namespace Planvexa.Modules.Collaboration.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Collaboration.Domain;

public sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> b)
    {
        b.ToTable("comments", CollaborationModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Body).HasMaxLength(10000);
        b.Property(x => x.IdempotencyKey).HasMaxLength(200);
        b.HasIndex(x => new { x.WorkspaceId, x.TaskId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.WorkspaceId, x.ParentId });

        // Offline-mutation-outbox replay guard: unique per workspace when set (see IdempotencyKey's doc comment).
        b.HasIndex(x => new { x.WorkspaceId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");

        b.HasMany(x => x.Mentions).WithOne().HasForeignKey(m => m.CommentId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Reactions).WithOne().HasForeignKey(r => r.CommentId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Mentions).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Reactions).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class MentionConfiguration : IEntityTypeConfiguration<Mention>
{
    public void Configure(EntityTypeBuilder<Mention> b)
    {
        b.ToTable("mentions", CollaborationModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.WorkspaceId, x.MentionedUserId });
        b.HasIndex(x => new { x.WorkspaceId, x.CommentId, x.MentionedUserId }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class CommentReactionConfiguration : IEntityTypeConfiguration<CommentReaction>
{
    public void Configure(EntityTypeBuilder<CommentReaction> b)
    {
        b.ToTable("comment_reactions", CollaborationModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Emoji).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.CommentId, x.UserId, x.Emoji }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class CommentAttachmentConfiguration : IEntityTypeConfiguration<CommentAttachment>
{
    public void Configure(EntityTypeBuilder<CommentAttachment> b)
    {
        b.ToTable("comment_attachments", CollaborationModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(200).IsRequired();
        b.Property(x => x.StoragePath).HasMaxLength(500).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.CommentId });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class PublicCommentConfiguration : IEntityTypeConfiguration<PublicComment>
{
    /// <summary>Guest comments live alongside share links in the <c>sharing</c> schema (owned by the Collaboration module).</summary>
    public void Configure(EntityTypeBuilder<PublicComment> b)
    {
        b.ToTable("public_comments", PublicShareLinkConfiguration.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.GuestName).HasMaxLength(120);
        b.Property(x => x.Body).HasMaxLength(10000).IsRequired();
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.HasIndex(x => new { x.WorkspaceId, x.ShareLinkId, x.CreatedAtUtc });
        b.Ignore(x => x.DomainEvents);
    }
}
