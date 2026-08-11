namespace Planvexa.Modules.Collaboration.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// A comment left by an anonymous visitor through a Comment-level <see cref="PublicShareLink"/>. Kept
/// separate from the internal <see cref="Comment"/> aggregate rather than reusing it: <see cref="Comment"/>
/// requires a real workspace-member <c>AuthorUserId</c> (it drives mention validation and notification
/// fan-out to real users), which an anonymous guest does not have. This is display-only for the link
/// owner (see ShareLinkService.ListPublicCommentsAsync) — there is no anonymous edit/delete path, so
/// "view + comment, never edit" holds by construction.
/// </summary>
public sealed class PublicComment : Entity, IAggregateRoot, IWorkspaceOwned
{
    private PublicComment()
    {
    }

    private PublicComment(
        Guid id, Guid workspaceId, Guid shareLinkId, Guid taskId, string? guestName, string body, DateTimeOffset nowUtc, string? ipAddress)
        : base(id)
    {
        WorkspaceId = workspaceId;
        ShareLinkId = shareLinkId;
        TaskId = taskId;
        GuestName = guestName;
        Body = body;
        CreatedAtUtc = nowUtc;
        IpAddress = ipAddress;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid ShareLinkId { get; private set; }
    public Guid TaskId { get; private set; }
    public string? GuestName { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string? IpAddress { get; private set; }

    public static PublicComment Create(
        Guid id, Guid workspaceId, Guid shareLinkId, Guid taskId, string? guestName, string body, DateTimeOffset nowUtc, string? ipAddress)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(shareLinkId, nameof(shareLinkId));
        Guard.AgainstEmpty(taskId, nameof(taskId));
        Guard.AgainstNullOrWhiteSpace(body, nameof(body));

        var trimmedName = string.IsNullOrWhiteSpace(guestName) ? null : guestName.Trim();
        return new PublicComment(id, workspaceId, shareLinkId, taskId, trimmedName, body.Trim(), nowUtc, ipAddress);
    }
}
