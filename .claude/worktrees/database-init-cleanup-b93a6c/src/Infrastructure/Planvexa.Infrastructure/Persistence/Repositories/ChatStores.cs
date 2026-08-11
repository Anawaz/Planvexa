namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Chat.Application;
using Planvexa.Modules.Chat.Domain;

internal sealed class ChatChannelStore(PlanvexaDbContext db) : IChatChannelStore
{
    public void Add(ChatChannel channel) => db.Set<ChatChannel>().Add(channel);

    public Task<ChatChannel?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<ChatChannel>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<ChatChannel?> FindWithMembersAsync(Guid id, CancellationToken ct = default)
        => db.Set<ChatChannel>().Include(c => c.Members).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<ChatChannel>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<ChatChannel>().Include(c => c.Members)
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.Name).ToListAsync(ct);

    public async Task<ChatChannel?> FindDirectMessageAsync(
        Guid workspaceId, ChatChannelType channelType, IReadOnlyCollection<Guid> participantUserIds, CancellationToken ct = default)
    {
        var wanted = participantUserIds.Distinct().OrderBy(x => x).ToList();

        // Dm/GroupDm channels per workspace are small in number; an in-memory exact-set match is simpler
        // and cheap enough here than a set-matching SQL query.
        var candidates = await db.Set<ChatChannel>().Include(c => c.Members)
            .Where(x => x.WorkspaceId == workspaceId && x.ChannelType == channelType)
            .ToListAsync(ct);

        return candidates.FirstOrDefault(c =>
        {
            var memberIds = c.Members.Select(m => m.UserId).OrderBy(x => x).ToList();
            return memberIds.Count == wanted.Count && memberIds.SequenceEqual(wanted);
        });
    }
}

internal sealed class ChatMessageStore(PlanvexaDbContext db) : IChatMessageStore
{
    public void Add(ChatMessage message) => db.Set<ChatMessage>().Add(message);

    public Task<ChatMessage?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<ChatMessage>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<ChatMessage?> FindWithChildrenAsync(Guid id, CancellationToken ct = default)
        => db.Set<ChatMessage>().Include(m => m.Mentions).Include(m => m.Reactions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<ChatMessage>> ListByChannelAsync(Guid channelId, DateTimeOffset? beforeUtc, int max, CancellationToken ct = default)
    {
        var query = db.Set<ChatMessage>().Include(m => m.Mentions).Include(m => m.Reactions)
            .Where(x => x.ChannelId == channelId);
        if (beforeUtc is { } before)
        {
            query = query.Where(x => x.CreatedAtUtc < before);
        }

        var pageSize = max is > 0 and <= 200 ? max : 100;

        // Fetch the newest page, then return in chronological order for display.
        var page = await query.OrderByDescending(x => x.CreatedAtUtc).Take(pageSize).ToListAsync(ct);
        return page.OrderBy(x => x.CreatedAtUtc).ToList();
    }

    public async Task<IReadOnlyList<ChatMessage>> SearchByWorkspaceAsync(Guid workspaceId, string contains, int take, CancellationToken ct = default)
        => await db.Set<ChatMessage>()
            .Where(x => x.WorkspaceId == workspaceId && !x.IsDeleted && EF.Functions.ILike(x.Body, contains))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);

    public Task<int> CountAfterAsync(Guid channelId, DateTimeOffset? afterUtc, CancellationToken ct = default)
    {
        var query = db.Set<ChatMessage>().Where(x => x.ChannelId == channelId && !x.IsDeleted);
        if (afterUtc is { } after)
        {
            query = query.Where(x => x.CreatedAtUtc > after);
        }

        return query.CountAsync(ct);
    }
}

internal sealed class ChatAttachmentStore(PlanvexaDbContext db) : IChatAttachmentStore
{
    public void Add(ChatAttachment attachment) => db.Set<ChatAttachment>().Add(attachment);

    public Task<ChatAttachment?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<ChatAttachment>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<ChatAttachment>> ListForMessageAsync(Guid messageId, CancellationToken ct = default)
        => await db.Set<ChatAttachment>().Where(x => x.MessageId == messageId).OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);

    public async Task<IReadOnlyList<ChatAttachment>> ListForMessagesAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
        => await db.Set<ChatAttachment>().Where(x => messageIds.Contains(x.MessageId)).OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);

    public void Remove(ChatAttachment attachment) => db.Set<ChatAttachment>().Remove(attachment);
}

internal sealed class ChatChannelReadStateStore(PlanvexaDbContext db) : IChatChannelReadStateStore
{
    public void Add(ChatChannelReadState state) => db.Set<ChatChannelReadState>().Add(state);

    public Task<ChatChannelReadState?> FindAsync(Guid channelId, Guid userId, CancellationToken ct = default)
        => db.Set<ChatChannelReadState>().FirstOrDefaultAsync(x => x.ChannelId == channelId && x.UserId == userId, ct);

    public async Task<IReadOnlyList<ChatChannelReadState>> ListForUserAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
        => await db.Set<ChatChannelReadState>().Where(x => x.WorkspaceId == workspaceId && x.UserId == userId).ToListAsync(ct);
}
