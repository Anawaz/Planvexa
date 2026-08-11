namespace Planvexa.Modules.Collaboration.Application;

using Planvexa.Modules.Collaboration.Domain;
using Planvexa.SharedContracts.Workspaces;

public interface IShareLinkStore
{
    void Add(PublicShareLink link);
    Task<PublicShareLink?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Cross-workspace lookup by token hash (anonymous read path — the token is the credential).</summary>
    Task<PublicShareLink?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    Task<IReadOnlyList<PublicShareLink>> ListForTaskAsync(Guid taskId, CancellationToken ct = default);
}

public interface IPublicCommentStore
{
    void Add(PublicComment comment);
    Task<IReadOnlyList<PublicComment>> ListForShareLinkAsync(Guid shareLinkId, CancellationToken ct = default);
}

public sealed record ShareLinkDto(
    Guid Id, Guid TaskId, string Token, string Url, DateTimeOffset? ExpiresAtUtc, bool RequiresPassword, PermissionLevel PermissionLevel);

public sealed record SharedTaskDto(Guid TaskId, string Title, string? Description, bool IsCompleted, bool AllowsComments);

public sealed record PublicCommentDto(Guid Id, string? GuestName, string Body, DateTimeOffset CreatedAtUtc, string? IpAddress);

public sealed record ShareAccessLogEntryDto(Guid Id, string Action, DateTimeOffset CreatedAtUtc, string? IpAddress);

/// <summary>Outcome of an anonymous public share-link lookup, distinguishing "no such link" from "wrong/missing password".</summary>
public enum ShareLinkAccessStatus
{
    NotFound,
    PasswordRequired,
    InvalidPassword,
    Ok,
}

public sealed record SharedTaskAccessResult(ShareLinkAccessStatus Status, SharedTaskDto? Task)
{
    public static readonly SharedTaskAccessResult NotFound = new(ShareLinkAccessStatus.NotFound, null);
    public static readonly SharedTaskAccessResult PasswordRequired = new(ShareLinkAccessStatus.PasswordRequired, null);
    public static readonly SharedTaskAccessResult InvalidPassword = new(ShareLinkAccessStatus.InvalidPassword, null);
}

/// <summary>Outcome of an anonymous public-comment submission, on top of the same link resolution as <see cref="ShareLinkAccessStatus"/>.</summary>
public enum PublicCommentPostStatus
{
    NotFound,
    PasswordRequired,
    InvalidPassword,

    /// <summary>The link is valid but only grants View, not Comment.</summary>
    Forbidden,
    Invalid,
    Ok,
}

public sealed record PublicCommentPostResult(PublicCommentPostStatus Status, PublicCommentDto? Comment)
{
    public static readonly PublicCommentPostResult NotFound = new(PublicCommentPostStatus.NotFound, null);
    public static readonly PublicCommentPostResult PasswordRequired = new(PublicCommentPostStatus.PasswordRequired, null);
    public static readonly PublicCommentPostResult InvalidPassword = new(PublicCommentPostStatus.InvalidPassword, null);
    public static readonly PublicCommentPostResult Forbidden = new(PublicCommentPostStatus.Forbidden, null);
    public static readonly PublicCommentPostResult Invalid = new(PublicCommentPostStatus.Invalid, null);

    public static PublicCommentPostResult Ok(PublicCommentDto comment) => new(PublicCommentPostStatus.Ok, comment);
}
