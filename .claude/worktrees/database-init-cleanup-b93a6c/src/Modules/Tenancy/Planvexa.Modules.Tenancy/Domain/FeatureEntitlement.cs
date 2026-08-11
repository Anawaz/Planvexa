namespace Planvexa.Modules.Tenancy.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>Workspace feature setting for optional capabilities.</summary>
public sealed class FeatureEntitlement : Entity, IWorkspaceOwned
{
    private FeatureEntitlement()
    {
    }

    private FeatureEntitlement(Guid id, Guid workspaceId, string featureKey, bool isEnabled, long? limit, string source)
        : base(id)
    {
        WorkspaceId = workspaceId;
        FeatureKey = featureKey;
        IsEnabled = isEnabled;
        Limit = limit;
        Source = source;
    }

    public Guid WorkspaceId { get; private set; }
    public string FeatureKey { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; }

    /// <summary>Optional numeric quota (e.g. max automations). Null means unlimited/not applicable.</summary>
    public long? Limit { get; private set; }

    /// <summary>What granted this entitlement (e.g. "plan:free", "addon:ai").</summary>
    public string Source { get; private set; } = string.Empty;

    public static FeatureEntitlement Grant(Guid id, Guid workspaceId, string featureKey, bool isEnabled, long? limit, string source)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(workspaceId, nameof(workspaceId));
        Guard.AgainstNullOrWhiteSpace(featureKey, nameof(featureKey));
        return new FeatureEntitlement(id, workspaceId, featureKey, isEnabled, limit, source);
    }

    public void Update(bool isEnabled, long? limit, string source)
    {
        IsEnabled = isEnabled;
        Limit = limit;
        Source = source;
    }
}
