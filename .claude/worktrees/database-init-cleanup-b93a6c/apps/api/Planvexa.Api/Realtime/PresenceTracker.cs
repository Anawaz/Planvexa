namespace Planvexa.Api.Realtime;

using System.Collections.Concurrent;

/// <summary>
/// Tracks which users are currently connected to each workspace group. In-memory for a single node
/// (a distributed backplane replaces this when SignalR is scaled out). Keyed by the realtime group
/// name (<c>tenant:workspace</c>).
/// </summary>
public sealed class PresenceTracker
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, int>> _groups = new();

    /// <summary>Records a user's connection to a group. Returns true if the user became newly present.</summary>
    public bool Add(string group, Guid userId)
    {
        var users = _groups.GetOrAdd(group, _ => new ConcurrentDictionary<Guid, int>());
        var newlyPresent = false;
        users.AddOrUpdate(userId, _ => { newlyPresent = true; return 1; }, (_, count) => count + 1);
        return newlyPresent;
    }

    /// <summary>Removes one connection. Returns true if the user is no longer present in the group.</summary>
    public bool Remove(string group, Guid userId)
    {
        if (!_groups.TryGetValue(group, out var users))
        {
            return false;
        }

        var nowAbsent = false;
        users.AddOrUpdate(userId, _ => 0, (_, count) => count - 1);
        if (users.TryGetValue(userId, out var remaining) && remaining <= 0)
        {
            users.TryRemove(userId, out _);
            nowAbsent = true;
        }

        return nowAbsent;
    }

    public IReadOnlyList<Guid> UsersIn(string group)
        => _groups.TryGetValue(group, out var users) ? users.Keys.ToList() : Array.Empty<Guid>();
}
