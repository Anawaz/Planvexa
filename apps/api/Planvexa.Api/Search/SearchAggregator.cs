namespace Planvexa.Api.Search;

using Planvexa.SharedContracts.Search;

/// <summary>
/// Cross-module search fan-out. apps/api is the composition root and may reference every
/// module, so this calls each registered <see cref="ISearchProvider"/> directly — same multi-registration
/// DI shape <c>IResourceHierarchyQuery</c> already uses inside Tenancy's ACL resolver, just consumed here
/// at the edge instead of inside a module. Every provider is expected to have already permission-filtered
/// its own results (see ISearchProvider's doc comment); this class only does term validation, per-provider
/// fault isolation, and capping the merged set.
/// </summary>
public sealed class SearchAggregator(IEnumerable<ISearchProvider> providers, ILogger<SearchAggregator> logger)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    /// <summary>Shorter terms match almost everything, so they are answered with nothing.</summary>
    private const int MinTermLength = 2;

    /// <summary>Longer terms cannot match anything a title/name/body holds; truncating keeps every
    /// provider's underlying LIKE/Contains bounded.</summary>
    private const int MaxTermLength = 128;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string? term, int? limit, CancellationToken cancellationToken)
    {
        var trimmed = (term ?? string.Empty).Trim();
        if (trimmed.Length < MinTermLength)
        {
            return [];
        }

        if (trimmed.Length > MaxTermLength)
        {
            trimmed = trimmed[..MaxTermLength];
        }

        var cappedLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        var hits = new List<SearchHit>();
        foreach (var provider in providers)
        {
            try
            {
                hits.AddRange(await provider.SearchAsync(trimmed, cappedLimit, cancellationToken));
            }
            catch (Exception ex)
            {
                // One provider failing (e.g. a transient DB blip in one module) must not 500 the whole
                // search — every other module's results are still useful, and this is easy to spot in logs.
                logger.LogWarning(ex, "Search provider {Provider} failed", provider.GetType().Name);
            }
        }

        return hits.Take(cappedLimit).ToList();
    }
}
