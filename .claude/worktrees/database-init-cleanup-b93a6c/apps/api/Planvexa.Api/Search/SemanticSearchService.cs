namespace Planvexa.Api.Search;

using Planvexa.BuildingBlocks.Primitives;
using Planvexa.Modules.Ai.Application.Services;
using Planvexa.Modules.Ai.Authorization;
using Planvexa.SharedContracts.Search;

/// <summary>
/// AI-flavored "semantic search". Per the roadmap's explicit architectural constraint, this is a
/// RANKING layer on top of <see cref="SearchAggregator"/> — the already permission-filtered
/// cross-module search fan-out — never a parallel/unfiltered retrieval path. It over-fetches from the
/// aggregator (which does its own permission filtering per result type) and only re-orders what the
/// aggregator already decided the caller may see; it can narrow that set, never widen it.
///
/// The reranking itself is a deterministic token-overlap heuristic (<see cref="TextSimilarity"/>), which is
/// also exactly what runs when no AI provider is configured — there is no separate "offline" code path to
/// keep in sync, and no outbound call is ever made (so nothing here needs the redaction pass).
/// A real embeddings-based reranking mode (when the configured provider exposes an embeddings endpoint) is
/// not implemented — a known scope gap.
/// </summary>
public sealed class SemanticSearchService(SearchAggregator aggregator, AiServiceContext aiCtx)
{
    private const int OverfetchMultiplier = 3;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string? term, int? limit, CancellationToken ct)
    {
        var workspace = aiCtx.WorkspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            return [];
        }

        var role = (await aiCtx.Access.GetAccessAsync(workspace.WorkspaceId, aiCtx.CurrentUser.UserId, ct))?.Role;
        AiAuthorizer.EnsureUse(role);

        var cappedLimit = Math.Clamp(limit ?? SearchAggregator.DefaultLimit, 1, SearchAggregator.MaxLimit);
        var candidates = await aggregator.SearchAsync(term, cappedLimit * OverfetchMultiplier, ct);
        var trimmedTerm = (term ?? string.Empty).Trim();

        return candidates
            .Select(hit => (Hit: hit, Score: Score(hit, trimmedTerm)))
            .OrderByDescending(x => x.Score)
            .Take(cappedLimit)
            .Select(x => x.Hit)
            .ToList();
    }

    private static double Score(SearchHit hit, string term)
    {
        var titleScore = TextSimilarity.Jaccard(hit.Title, term);
        var subtitleScore = hit.Subtitle is null ? 0d : TextSimilarity.Jaccard(hit.Subtitle, term);
        var exactTitleBoost = hit.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1d : 0d;
        return titleScore * 2 + subtitleScore + exactTitleBoost;
    }
}
