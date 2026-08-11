namespace Planvexa.Modules.Chat.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// A user's last-read position in a channel, used to compute unread counts for the sidebar. One row per
/// (workspace, channel, user); upserted whenever the user views the channel.
/// </summary>
public sealed class ChatChannelReadState : Entity, IWorkspaceOwned
{
    private ChatChannelReadState()
    {
    }

    public ChatChannelReadState(
        Guid id, Guid workspaceId, Guid channelId, Guid userId, Guid? lastReadMessageId, DateTimeOffset lastReadAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        ChannelId = channelId;
        UserId = userId;
        LastReadMessageId = lastReadMessageId;
        LastReadAtUtc = lastReadAtUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid ChannelId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? LastReadMessageId { get; private set; }
    public DateTimeOffset LastReadAtUtc { get; private set; }

    public void MarkRead(Guid? lastReadMessageId, DateTimeOffset nowUtc)
    {
        LastReadMessageId = lastReadMessageId;
        LastReadAtUtc = nowUtc;
    }
}
