namespace Planvexa.SharedContracts.Tenancy;

/// <summary>A workspace's entitlement for a feature: whether it is enabled and an optional numeric limit.</summary>
public sealed record Entitlement(string FeatureKey, bool IsEnabled, long? Limit);

/// <summary>
/// Contract (implemented by the Tenancy module) exposing a workspace's feature entitlement, so other modules
/// can gate features (e.g. AI credits) without depending on Tenancy internals (AGENTS.md rule 7).
/// Returns a disabled entitlement when the feature is not granted.
/// </summary>
public interface IEntitlementQuery
{
    Task<Entitlement> GetAsync(Guid workspaceId, string featureKey, CancellationToken cancellationToken = default);
}
