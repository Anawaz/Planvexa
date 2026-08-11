namespace Planvexa.Api.Realtime;

using Microsoft.AspNetCore.SignalR;
using Planvexa.BuildingBlocks.Abstractions;

/// <summary>
/// SignalR implementation of <see cref="IRealtimeNotifier"/>. Broadcasts change signals to the
/// workspace group so only members of that workspace receive them. Registered in the API host,
/// overriding the infrastructure's no-op default.
/// </summary>
public sealed class SignalRRealtimeNotifier(IHubContext<WorkspaceHub> hub) : IRealtimeNotifier
{
    public Task NotifyAsync(RealtimeEvent realtimeEvent, CancellationToken cancellationToken = default)
    {
        var group = RealtimeGroups.Workspace(realtimeEvent.WorkspaceId);
        return hub.Clients.Group(group).SendAsync("entityChanged", realtimeEvent, cancellationToken);
    }
}
