namespace Planvexa.BuildingBlocks.Platform;

/// <summary>
/// Well-known platform actors. <see cref="ActorUserId"/> attributes background/automation-produced
/// writes to the system rather than an interactive user. The workflow event pipeline uses it to break
/// automation loops: events whose actor is the system actor are not re-dispatched to automations.
/// </summary>
public static class PlatformActors
{
    /// <summary>Sentinel user id for system/automation-produced state changes.</summary>
    public static readonly Guid System = new("00000000-0000-0000-0000-0000000000a1");
}
