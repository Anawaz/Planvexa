namespace Planvexa.Modules.Tenancy.Application;

/// <summary>Workspace feature defaults for optional capabilities.</summary>
public static class PlanCatalog
{
    public sealed record FeatureGrant(string Key, bool Enabled, long? Limit);

    private static readonly IReadOnlyList<FeatureGrant> Defaults = new List<FeatureGrant>
    {
        new("workspaces", true, 3),
        new("members", true, 10),
        new("guests", true, 5),
        new("storage_mb", true, 1024),
        new("automations", true, 50),
        new("time_tracking", true, null),
        new("integrations", true, 2),
        new("ai_credits", false, 0),
    };

    public static IReadOnlyList<FeatureGrant> DefaultsForWorkspace() => Defaults;
}
