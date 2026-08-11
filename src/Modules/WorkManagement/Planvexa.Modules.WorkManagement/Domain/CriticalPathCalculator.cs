namespace Planvexa.Modules.WorkManagement.Domain;

/// <summary>
/// Standard Critical Path Method (CPM) over a task dependency DAG -- the longest chain of
/// dependent durations end to end. Pure function (no I/O), used by ViewQueryService.GanttAsync to flag
/// each GanttBarDto as on/off the critical path. See CriticalPathCalculatorTests for worked examples.
/// </summary>
public static class CriticalPathCalculator
{
    public sealed record Node(Guid Id, DateTimeOffset? StartDate, DateTimeOffset? DueDate, IReadOnlyList<Guid> DependsOnIds);

    /// <summary>
    /// Returns the ids on the critical path (zero slack/float). A task with no Start/Due date gets a
    /// nominal 1-day duration so it still participates in the chain rather than being silently dropped.
    /// A dependency cycle makes CPM undefined for the tasks inside it (and anything transitively
    /// downstream of it, since their earliest start can never be resolved) -- those tasks are excluded
    /// from the result rather than the whole computation throwing on bad data.
    /// </summary>
    public static IReadOnlySet<Guid> Compute(IReadOnlyList<Node> nodes)
    {
        if (nodes.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var byId = nodes.ToDictionary(n => n.Id);
        var duration = nodes.ToDictionary(n => n.Id, DurationDays);

        var successors = nodes.ToDictionary(n => n.Id, _ => new List<Guid>());
        var inDegree = nodes.ToDictionary(n => n.Id, _ => 0);
        foreach (var node in nodes)
        {
            foreach (var dep in node.DependsOnIds.Distinct())
            {
                if (dep == node.Id || !byId.ContainsKey(dep))
                {
                    continue; // self-reference or dangling id: ignore rather than let bad data break CPM
                }

                successors[dep].Add(node.Id);
                inDegree[node.Id]++;
            }
        }

        // Kahn's algorithm for topological order; any id never dequeued is part of a cycle (or
        // downstream of one) and is simply left out of `order`, hence out of the result below.
        var order = new List<Guid>();
        var remainingInDegree = new Dictionary<Guid, int>(inDegree);
        var queue = new Queue<Guid>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            order.Add(id);
            foreach (var successor in successors[id])
            {
                if (--remainingInDegree[successor] == 0)
                {
                    queue.Enqueue(successor);
                }
            }
        }

        if (order.Count == 0)
        {
            return new HashSet<Guid>();
        }

        // Forward pass: earliest start/finish, in topological order.
        var earlyStart = new Dictionary<Guid, int>();
        var earlyFinish = new Dictionary<Guid, int>();
        foreach (var id in order)
        {
            var resolvedDeps = byId[id].DependsOnIds.Where(earlyFinish.ContainsKey).ToList();
            earlyStart[id] = resolvedDeps.Count == 0 ? 0 : resolvedDeps.Max(d => earlyFinish[d]);
            earlyFinish[id] = earlyStart[id] + duration[id];
        }

        var projectFinish = earlyFinish.Values.Max();

        // Backward pass: latest start/finish, in reverse topological order.
        var lateStart = new Dictionary<Guid, int>();
        var lateFinish = new Dictionary<Guid, int>();
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var id = order[i];
            var resolvedSuccessors = successors[id].Where(lateStart.ContainsKey).ToList();
            lateFinish[id] = resolvedSuccessors.Count == 0 ? projectFinish : resolvedSuccessors.Min(s => lateStart[s]);
            lateStart[id] = lateFinish[id] - duration[id];
        }

        return order.Where(id => lateStart[id] - earlyStart[id] == 0).ToHashSet();
    }

    private static int DurationDays(Node node)
    {
        if (node.StartDate is { } start && node.DueDate is { } end)
        {
            return Math.Max((int)Math.Round((end - start).TotalDays, MidpointRounding.AwayFromZero), 0);
        }

        return 1;
    }
}
