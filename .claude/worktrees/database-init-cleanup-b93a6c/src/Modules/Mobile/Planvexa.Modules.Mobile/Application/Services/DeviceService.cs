namespace Planvexa.Modules.Mobile.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Mobile.Authorization;
using Planvexa.Modules.Mobile.Domain;

public sealed class DeviceService(MobileServiceContext ctx, IDeviceRegistrationStore devices)
    : MobileServiceBase(ctx)
{
    public async Task<IReadOnlyList<DeviceDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        MobileAuthorizer.EnsureUse((await AccessAsync(workspaceId, ct))?.Role);

        var list = await devices.ListForUserAsync(workspaceId, UserId, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<DeviceDto> RegisterAsync(RegisterDeviceCommand cmd, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        MobileAuthorizer.EnsureUse((await AccessAsync(workspaceId, ct))?.Role);

        if (!Enum.TryParse<DevicePlatform>(cmd.Platform, ignoreCase: true, out var platform) || !Enum.IsDefined(platform))
        {
            throw new ValidationAppException("Unsupported mobile device platform.");
        }

        var tokenHash = DeviceRegistration.HashToken(cmd.PushToken);
        var device = await devices.FindByTokenHashAsync(workspaceId, UserId, tokenHash, ct);
        if (device is not null)
        {
            device.Touch(Now);
            await SaveAsync(ct);
            return ToDto(device);
        }

        device = DeviceRegistration.Register(NewId(), workspaceId, UserId, platform, cmd.PushToken, cmd.AppVersion, Now, cmd.Endpoint, cmd.P256dh, cmd.Auth);
        devices.Add(device);
        Audit("mobile.device.registered", "DeviceRegistration", device.Id, new { platform });
        await SaveAsync(ct);
        return ToDto(device);
    }

    public async Task UnregisterAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        MobileAuthorizer.EnsureUse((await AccessAsync(workspaceId, ct))?.Role);

        var device = await devices.FindAsync(id, ct)
            ?? throw new NotFoundException("Device registration not found.");
        if (device.UserId != UserId)
        {
            throw new ForbiddenException("You can only remove your own devices.");
        }

        devices.Remove(device);
        Audit("mobile.device.unregistered", "DeviceRegistration", id);
        await SaveAsync(ct);
    }

    private static DeviceDto ToDto(DeviceRegistration d)
        => new(d.Id, d.Platform.ToString(), d.AppVersion, d.LastSeenAtUtc, d.CreatedAtUtc);
}
