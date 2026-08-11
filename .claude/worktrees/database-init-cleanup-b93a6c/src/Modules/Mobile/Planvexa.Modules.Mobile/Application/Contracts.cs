namespace Planvexa.Modules.Mobile.Application;

// ---- DTOs ----
public sealed record DeviceDto(Guid Id, string Platform, string? AppVersion, DateTimeOffset LastSeenAtUtc, DateTimeOffset CreatedAtUtc);

public sealed record SyncChangeDto(
    Guid TaskId, Guid ListId, Guid SpaceId, string Title, string Priority, bool IsCompleted,
    bool IsDeleted, DateTimeOffset? DueDate, DateTimeOffset ChangedAtUtc);

public sealed record SyncResultDto(IReadOnlyList<SyncChangeDto> Changes, DateTimeOffset NextCursorUtc);

// ---- Commands ----
public sealed record RegisterDeviceCommand(
    string Platform, string PushToken, string? AppVersion,
    string? Endpoint = null, string? P256dh = null, string? Auth = null);
