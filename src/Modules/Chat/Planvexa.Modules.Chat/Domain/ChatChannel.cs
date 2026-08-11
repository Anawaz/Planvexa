namespace Planvexa.Modules.Chat.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// Discriminates what a <see cref="ChatChannel"/> represents. Workspace/Private are the
/// original two flavors (still governed by <see cref="ChatChannel.IsPrivate"/> + explicit membership);
/// Space/List/Task are linked to a WorkManagement resource (see <see cref="ChatChannel.LinkedResourceType"/>)
/// whose ACL gates access in ADDITION to the workspace-role floor (checked in the application layer, not
/// here, since it requires an async cross-module call — see ChatChannelService.CanAccessAsync); Dm/GroupDm
/// are membership-only (no linked resource, always <see cref="ChatChannel.IsPrivate"/> = true so the
/// existing membership-gated <see cref="ChatChannel.CanBeAccessedBy"/> check already excludes the
/// "any workspace member" fallback for them).
/// </summary>
public enum ChatChannelType
{
    Workspace = 0,
    Space = 1,
    List = 2,
    Task = 3,
    Private = 4,
    Dm = 5,
    GroupDm = 6,
}

/// <summary>
/// Resource-type strings a channel can link to. Must exactly match WorkManagement's own
/// <c>WorkResourceTypes</c> constants — Chat cannot reference that module directly (AGENTS.md rule 7: no
/// cross-module table/type reads), and <see cref="Planvexa.SharedContracts.Workspaces.IResourcePermissionQuery"/>
/// takes free-form strings by design so each owning module can register its own resource types.
/// </summary>
public static class ChatLinkedResourceTypes
{
    public const string Space = "space";
    public const string List = "list";
    public const string Task = "task";
}

/// <summary>
/// A workspace chat channel. Public (Workspace-type) channels are readable/postable by any workspace
/// member; Private/Dm/GroupDm channels are restricted to their explicit members; Space/List/Task-linked
/// channels additionally require the caller to hold at least View on the linked resource (enforced by
/// ChatChannelService, which is the only place with access to the cross-module ACL resolver). The channel
/// owns its membership via the aggregate.
/// </summary>
public sealed class ChatChannel : Entity, IAggregateRoot, IWorkspaceOwned
{
    private readonly List<ChatChannelMember> _members = new();

    private ChatChannel()
    {
    }

    private ChatChannel(
        Guid id, Guid workspaceId, ChatChannelType channelType, string name, string? description, bool isPrivate,
        string? linkedResourceType, Guid? linkedResourceId, Guid createdBy, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        ChannelType = channelType;
        Name = name;
        Description = description;
        IsPrivate = isPrivate;
        LinkedResourceType = linkedResourceType;
        LinkedResourceId = linkedResourceId;
        CreatedByUserId = createdBy;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public ChatChannelType ChannelType { get; private set; } = ChatChannelType.Workspace;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsPrivate { get; private set; }

    /// <summary>Set together with <see cref="LinkedResourceId"/> only when <see cref="ChannelType"/> is
    /// Space/List/Task; one of <see cref="ChatLinkedResourceTypes"/>.</summary>
    public string? LinkedResourceType { get; private set; }
    public Guid? LinkedResourceId { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public bool IsArchived => ArchivedAtUtc is not null;

    public IReadOnlyList<ChatChannelMember> Members => _members.AsReadOnly();

    public static ChatChannel Create(
        Guid id, Guid workspaceId, string name, string? description, bool isPrivate,
        Guid createdBy, Func<Guid> memberIdFactory, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstEmpty(createdBy, nameof(createdBy));

        var type = isPrivate ? ChatChannelType.Private : ChatChannelType.Workspace;
        var channel = new ChatChannel(id, workspaceId, type, name.Trim(), Normalize(description), isPrivate, null, null, createdBy, nowUtc);

        // The creator is always a member (relevant for private channels).
        channel._members.Add(ChatChannelMember.Create(memberIdFactory(), channel.Id, createdBy, nowUtc));
        return channel;
    }

    /// <summary>Creates a channel linked to a Space/List/Task. Not private by itself — access is gated by
    /// the linked resource's ACL, resolved asynchronously by ChatChannelService, not here.</summary>
    public static ChatChannel CreateLinked(
        Guid id, Guid workspaceId, ChatChannelType type, string name, string? description,
        string linkedResourceType, Guid linkedResourceId, Guid createdBy, Func<Guid> memberIdFactory, DateTimeOffset nowUtc)
    {
        if (type is not (ChatChannelType.Space or ChatChannelType.List or ChatChannelType.Task))
        {
            throw new ValidationAppException("Linked channels must be a Space, List, or Task channel type.");
        }

        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstNullOrWhiteSpace(linkedResourceType, nameof(linkedResourceType));
        Guard.AgainstEmpty(linkedResourceId, nameof(linkedResourceId));
        Guard.AgainstEmpty(createdBy, nameof(createdBy));

        var channel = new ChatChannel(
            id, workspaceId, type, name.Trim(), Normalize(description), isPrivate: false,
            linkedResourceType, linkedResourceId, createdBy, nowUtc);
        channel._members.Add(ChatChannelMember.Create(memberIdFactory(), channel.Id, createdBy, nowUtc));
        return channel;
    }

    /// <summary>Creates a DM (exactly 2 participants) or group DM (3+). Always private/membership-gated,
    /// unnamed (frontend synthesizes a display name from the other participant(s)), no linked resource.</summary>
    public static ChatChannel CreateDirect(
        Guid id, Guid workspaceId, ChatChannelType type, IReadOnlyCollection<Guid> participantUserIds,
        Guid createdBy, Func<Guid> memberIdFactory, DateTimeOffset nowUtc)
    {
        if (type is not (ChatChannelType.Dm or ChatChannelType.GroupDm))
        {
            throw new ValidationAppException("Direct-message channels must be a Dm or GroupDm channel type.");
        }

        Guard.AgainstEmpty(createdBy, nameof(createdBy));
        var distinct = participantUserIds.Distinct().ToList();
        if (!distinct.Contains(createdBy))
        {
            throw new ValidationAppException("The creator must be one of the participants.");
        }

        if (type == ChatChannelType.Dm && distinct.Count != 2)
        {
            throw new ValidationAppException("A direct message must have exactly 2 participants.");
        }

        if (type == ChatChannelType.GroupDm && distinct.Count < 3)
        {
            throw new ValidationAppException("A group direct message needs at least 3 participants.");
        }

        var channel = new ChatChannel(id, workspaceId, type, string.Empty, null, isPrivate: true, null, null, createdBy, nowUtc);
        foreach (var userId in distinct)
        {
            channel._members.Add(ChatChannelMember.Create(memberIdFactory(), channel.Id, userId, nowUtc));
        }

        return channel;
    }

    public void UpdateDetails(string? name, string? description, DateTimeOffset nowUtc)
    {
        _ = nowUtc;
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name.Trim();
        }

        if (description is not null)
        {
            Description = Normalize(description);
        }
    }

    public void Archive(DateTimeOffset nowUtc)
    {
        ArchivedAtUtc ??= nowUtc;
    }

    public bool IsMember(Guid userId) => _members.Any(m => m.UserId == userId);

    public bool AddMember(Guid id, Guid userId, DateTimeOffset nowUtc)
    {
        if (_members.Any(m => m.UserId == userId))
        {
            return false;
        }

        _members.Add(ChatChannelMember.Create(id, Id, userId, nowUtc));
        return true;
    }

    public bool RemoveMember(Guid userId)
    {
        if (userId == CreatedByUserId)
        {
            throw new ValidationAppException("The channel creator cannot be removed from the channel.");
        }

        var existing = _members.FirstOrDefault(m => m.UserId == userId);
        if (existing is null)
        {
            return false;
        }

        _members.Remove(existing);
        return true;
    }

    /// <summary>Whether the user may access this channel, given their (already-resolved) workspace membership.</summary>
    public bool CanBeAccessedBy(Guid userId, bool isWorkspaceMember)
        => IsPrivate ? IsMember(userId) : isWorkspaceMember;

    private static string? Normalize(string? description)
        => string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}

/// <summary>Membership of a (typically private) chat channel.</summary>
public sealed class ChatChannelMember : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private ChatChannelMember()
    {
    }

    private ChatChannelMember(Guid id, Guid channelId, Guid userId, DateTimeOffset joinedAtUtc)
        : base(id)
    {
        ChannelId = channelId;
        UserId = userId;
        JoinedAtUtc = joinedAtUtc;
    }

    public Guid ChannelId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset JoinedAtUtc { get; private set; }

    public static ChatChannelMember Create(Guid id, Guid channelId, Guid userId, DateTimeOffset joinedAtUtc)
    {
        Guard.AgainstEmpty(userId, nameof(userId));
        return new ChatChannelMember(id, channelId, userId, joinedAtUtc);
    }
}
