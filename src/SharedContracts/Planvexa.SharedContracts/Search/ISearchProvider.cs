namespace Planvexa.SharedContracts.Search;

/// <summary>
/// One flat cross-module search hit. <c>ListId</c> is populated whenever the result deep-links into a
/// WorkManagement list (Task results, and Comment results which link to their owning task) and is null
/// otherwise.
/// </summary>
public sealed record SearchHit(string Type, Guid Id, string Title, string? Subtitle, Guid? ListId);

/// <summary>
/// Implemented once per module that owns a searchable resource type. The cross-module search aggregator
/// (apps/api's SearchAggregator) enumerates every registered provider and merges the results — same
/// multi-registration DI fan-out shape as <see cref="Workspaces.IResourceHierarchyQuery"/>.
///
/// SECURITY: every implementation MUST permission-filter its own results using its module's
/// real read-permission check before returning them. A result type that cannot be filtered correctly
/// must not be searchable at all rather than returned unfiltered — search fans out across every module,
/// so an unfiltered result type here is a confidentiality bug at the worst possible place (a feature
/// whose entire purpose is surfacing content, including content the caller could not otherwise reach).
///
/// <paramref name="term"/> is pre-trimmed and length-validated by the aggregator; implementations may use
/// it directly in a case-insensitive contains match. <paramref name="limit"/> is the max hits this
/// provider should return (the aggregator applies its own overall cap after merging).
/// </summary>
public interface ISearchProvider
{
    Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int limit, CancellationToken cancellationToken = default);
}
