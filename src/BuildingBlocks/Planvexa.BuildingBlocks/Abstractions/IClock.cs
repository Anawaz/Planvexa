namespace Planvexa.BuildingBlocks.Abstractions;

/// <summary>
/// Abstraction over the system clock so time-dependent logic is testable.
/// Always returns UTC. Never use <see cref="System.DateTime.Now"/> directly in domain code.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
