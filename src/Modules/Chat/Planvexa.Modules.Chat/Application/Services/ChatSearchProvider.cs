namespace Planvexa.Modules.Chat.Application.Services;

using Planvexa.Modules.Chat.Authorization;
using Planvexa.SharedContracts.Search;

/// <summary>
/// Cross-module search: channel-name and message-body matches over this workspace's chat,
/// filtered through <see cref="ChatChannelService.CanAccessAsync"/> — the exact same access rule
/// ChatChannelService.LoadForReadAsync applies for browsing, re-verified for the new channel types
/// (Space/List/Task-linked channels gated by the linked resource's ACL; Dm/GroupDm gated by strict
/// membership) — before a single channel name or message snippet is returned. See ISearchProvider's doc
/// comment on why this filter is not optional: a message in a channel the caller cannot access must never
/// appear here.
/// </summary>
public sealed class ChatSearchProvider(ChatServiceContext ctx, IChatChannelStore channels, IChatMessageStore messages, ChatChannelService channelService)
    : ChatServiceBase(ctx), ISearchProvider
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int limit, CancellationToken cancellationToken = default)
    {
        var workspace = Ctx.WorkspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            return [];
        }

        var role = (await AccessAsync(workspace.WorkspaceId, cancellationToken))?.Role;
        if (!ChatAuthorizer.CanRead(role))
        {
            return [];
        }

        var allChannels = await channels.ListByWorkspaceAsync(workspace.WorkspaceId, cancellationToken);
        var accessible = await channelService.FilterAccessibleAsync(allChannels, role, cancellationToken);
        var byId = accessible.ToDictionary(c => c.Id);

        var hits = new List<SearchHit>();

        foreach (var channel in accessible)
        {
            if (hits.Count >= limit)
            {
                return hits;
            }

            if (channel.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add(new SearchHit("ChatChannel", channel.Id, channel.Name, channel.IsPrivate ? "Private channel" : "Channel", null));
            }
        }

        if (hits.Count >= limit)
        {
            return hits;
        }

        var escaped = term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var contains = $"%{escaped}%";
        var messageBudget = (limit - hits.Count) * 3; // overfetch: most matches may be in inaccessible channels
        foreach (var message in await messages.SearchByWorkspaceAsync(workspace.WorkspaceId, contains, messageBudget, cancellationToken))
        {
            if (hits.Count >= limit)
            {
                break;
            }

            if (byId.TryGetValue(message.ChannelId, out var channel))
            {
                hits.Add(new SearchHit("ChatMessage", channel.Id, Snippet(message.Body), $"in #{channel.Name}", null));
            }
        }

        return hits;
    }

    private static string Snippet(string body) => body.Length <= 120 ? body : body[..120];
}
