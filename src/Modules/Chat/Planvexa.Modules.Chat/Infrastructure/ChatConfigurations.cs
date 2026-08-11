namespace Planvexa.Modules.Chat.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Chat.Domain;

public sealed class ChatChannelConfiguration : IEntityTypeConfiguration<ChatChannel>
{
    public void Configure(EntityTypeBuilder<ChatChannel> b)
    {
        b.ToTable("channels", ChatModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.ChannelType).HasConversion<int>().IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.LinkedResourceType).HasMaxLength(32);
        b.HasIndex(x => x.WorkspaceId);
        b.HasIndex(x => new { x.LinkedResourceType, x.LinkedResourceId });

        b.HasMany(x => x.Members).WithOne().HasForeignKey(m => m.ChannelId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.IsArchived);
    }
}

public sealed class ChatChannelMemberConfiguration : IEntityTypeConfiguration<ChatChannelMember>
{
    public void Configure(EntityTypeBuilder<ChatChannelMember> b)
    {
        b.ToTable("channel_members", ChatModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.ChannelId, x.UserId }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> b)
    {
        b.ToTable("messages", ChatModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        b.HasIndex(x => new { x.ChannelId, x.CreatedAtUtc });
        b.HasIndex(x => x.ParentMessageId);

        b.HasMany(x => x.Mentions).WithOne().HasForeignKey(m => m.MessageId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Reactions).WithOne().HasForeignKey(r => r.MessageId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Mentions).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Reactions).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ChatMentionConfiguration : IEntityTypeConfiguration<ChatMention>
{
    public void Configure(EntityTypeBuilder<ChatMention> b)
    {
        b.ToTable("mentions", ChatModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.WorkspaceId, x.MentionedUserId });
        b.HasIndex(x => new { x.MessageId, x.MentionedUserId }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ChatMessageReactionConfiguration : IEntityTypeConfiguration<ChatMessageReaction>
{
    public void Configure(EntityTypeBuilder<ChatMessageReaction> b)
    {
        b.ToTable("message_reactions", ChatModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Emoji).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.MessageId, x.UserId, x.Emoji }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ChatAttachmentConfiguration : IEntityTypeConfiguration<ChatAttachment>
{
    public void Configure(EntityTypeBuilder<ChatAttachment> b)
    {
        b.ToTable("attachments", ChatModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(200).IsRequired();
        b.Property(x => x.StoragePath).HasMaxLength(1000).IsRequired();
        b.HasIndex(x => x.MessageId);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ChatChannelReadStateConfiguration : IEntityTypeConfiguration<ChatChannelReadState>
{
    public void Configure(EntityTypeBuilder<ChatChannelReadState> b)
    {
        b.ToTable("channel_read_states", ChatModule.Schema);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.ChannelId, x.UserId }).IsUnique();
        b.HasIndex(x => new { x.WorkspaceId, x.UserId });
        b.Ignore(x => x.DomainEvents);
    }
}
