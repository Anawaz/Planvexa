namespace Planvexa.SharedContracts.Mobile;

/// <summary>
/// Contract (implemented in Infrastructure) that lets the Notifications module check whether a user has
/// at least one registered push-capable device, without touching the Mobile module's tables directly
/// (AGENTS.md rule 7). Used by <c>NotificationDeliveryProcessor</c> to decide whether a Push delivery is
/// eligible before invoking <c>IPushSender</c>.
/// </summary>
public interface IPushDeviceDirectory
{
    Task<bool> HasActiveDeviceAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default);
}
