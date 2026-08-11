namespace Planvexa.Modules.Mobile.Domain;

using System.Security.Cryptography;
using System.Text;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;

public sealed class DeviceRegistration : Entity, IWorkspaceOwned
{
    private DeviceRegistration()
    {
    }

    private DeviceRegistration(
        Guid id, Guid workspaceId, Guid userId, DevicePlatform platform, string tokenHash,
        string? appVersion, string? pushEndpoint, string? pushP256dh, string? pushAuth,
        DateTimeOffset createdAtUtc, DateTimeOffset lastSeenAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        Platform = platform;
        TokenHash = tokenHash;
        AppVersion = appVersion;
        PushEndpoint = pushEndpoint;
        PushP256dh = pushP256dh;
        PushAuth = pushAuth;
        CreatedAtUtc = createdAtUtc;
        LastSeenAtUtc = lastSeenAtUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public DevicePlatform Platform { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public string? AppVersion { get; private set; }

    /// <summary>gap-closer: the browser PushSubscription's own fields, stored RAW (unlike
    /// <see cref="TokenHash"/>) because a real Web Push sender must address/encrypt to the device with
    /// them -- they are not secret credentials like a password, so hashing would make them useless. Null
    /// for non-Web platforms or when the frontend did not supply a subscription. See
    /// Planvexa.Api.Notifications.LoggingPushSender's doc comment for what still turns this into real
    /// delivery (RFC 8291/8292).</summary>
    public string? PushEndpoint { get; private set; }
    public string? PushP256dh { get; private set; }
    public string? PushAuth { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public static DeviceRegistration Register(
        Guid id, Guid workspaceId, Guid userId, DevicePlatform platform, string rawPushToken,
        string? appVersion, DateTimeOffset nowUtc,
        string? pushEndpoint = null, string? pushP256dh = null, string? pushAuth = null)
    {
        Guard.AgainstNullOrWhiteSpace(rawPushToken, nameof(rawPushToken));
        var normalizedAppVersion = string.IsNullOrWhiteSpace(appVersion) ? null : appVersion.Trim();
        return new DeviceRegistration(
            id, workspaceId, userId, platform, HashToken(rawPushToken), normalizedAppVersion,
            NullIfBlank(pushEndpoint), NullIfBlank(pushP256dh), NullIfBlank(pushAuth), nowUtc, nowUtc);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    public void Touch(DateTimeOffset nowUtc) => LastSeenAtUtc = nowUtc;

    public static string HashToken(string rawToken)
    {
        var token = Guard.AgainstNullOrWhiteSpace(rawToken, nameof(rawToken));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
