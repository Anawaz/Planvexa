namespace Planvexa.Modules.Chat.Application;

using Planvexa.Modules.Chat.Domain;

public interface IChatChannelStore
{
    void Add(ChatChannel channel);
    Task<ChatChannel?> FindAsync(Guid id, CancellationToken ct = default);
    Task<ChatChannel?> FindWithMembersAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ChatChannel>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>Finds an existing Dm/GroupDm channel in this workspace whose membership is EXACTLY the
    /// given set of participants (order-independent), so starting a "new" DM with the same people reuses
    /// the existing thread instead of spawning a duplicate. Returns null if none exists.</summary>
    Task<ChatChannel?> FindDirectMessageAsync(Guid workspaceId, ChatChannelType channelType, IReadOnlyCollection<Guid> participantUserIds, CancellationToken ct = default);
}

public interface IChatMessageStore
{
    void Add(ChatMessage message);
    Task<ChatMessage?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Loads a message with its Mentions/Reactions navigations populated (needed before mutating
    /// either collection — same pattern as Collaboration's ICommentStore.FindWithChildrenAsync).</summary>
    Task<ChatMessage?> FindWithChildrenAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> ListByChannelAsync(Guid channelId, DateTimeOffset? beforeUtc, int max, CancellationToken ct = default);

    /// <summary>search: body matches across every (non-deleted) message in the workspace, newest
    /// first. Not scoped to a channel — the caller (ChatSearchProvider) filters per-channel access
    /// itself, this store does not know about channel membership/privacy.</summary>
    Task<IReadOnlyList<ChatMessage>> SearchByWorkspaceAsync(Guid workspaceId, string contains, int take, CancellationToken ct = default);

    /// <summary>Count of non-deleted messages in a channel created after a given point (or all of them,
    /// when null) — the unread-count primitive. Cheap enough for a channel-list sidebar since it is a
    /// single indexed COUNT, not a message fetch.</summary>
    Task<int> CountAfterAsync(Guid channelId, DateTimeOffset? afterUtc, CancellationToken ct = default);
}

public interface IChatAttachmentStore
{
    void Add(ChatAttachment attachment);
    Task<ChatAttachment?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ChatAttachment>> ListForMessageAsync(Guid messageId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatAttachment>> ListForMessagesAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);
    void Remove(ChatAttachment attachment);
}

public interface IChatChannelReadStateStore
{
    void Add(ChatChannelReadState state);
    Task<ChatChannelReadState?> FindAsync(Guid channelId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatChannelReadState>> ListForUserAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);
}
