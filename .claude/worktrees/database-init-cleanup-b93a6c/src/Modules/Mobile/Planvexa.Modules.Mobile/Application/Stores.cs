namespace Planvexa.Modules.Mobile.Application;

using Planvexa.Modules.Mobile.Domain;

public interface IDeviceRegistrationStore
{
    void Add(DeviceRegistration device);
    void Remove(DeviceRegistration device);
    Task<DeviceRegistration?> FindAsync(Guid id, CancellationToken ct = default);
    Task<DeviceRegistration?> FindByTokenHashAsync(Guid workspaceId, Guid userId, string tokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<DeviceRegistration>> ListForUserAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);
}
