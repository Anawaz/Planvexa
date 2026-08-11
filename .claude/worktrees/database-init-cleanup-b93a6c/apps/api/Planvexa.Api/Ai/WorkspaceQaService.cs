namespace Planvexa.Api.Ai;

using Planvexa.Api.Search;
using Planvexa.Modules.Ai.Application;
using Planvexa.Modules.Ai.Authorization;
using Planvexa.Modules.Ai.Domain;
using Planvexa.Modules.Ai.Application.Services;
using Planvexa.SharedContracts.Ai;
using Planvexa.SharedContracts.Search;

public sealed record AiAskDto(string Answer, IReadOnlyList<SearchHit> Sources, int TokensEstimated);

/// <summary>
/// Retrieval-augmented workspace question answering. THE load-bearing security property of this
/// class: it must never let the LLM "know" anything beyond what <see cref="SearchAggregator"/> — the
/// already permission-filtered cross-module search fan-out — would have returned for THIS requesting user.
/// It fetches candidate context via the aggregator only (never a raw table read), builds a numbered context
/// list from that context's own Title/Subtitle fields only, and both the real-provider system prompt
/// (<c>LiteLlmCompletionProvider.SystemPrompt</c>) and the offline fallback (<c>ExtractiveAi.AnswerFromContext</c>)
/// are constrained to answer only from that numbered list. Composed here (apps/api, the composition root)
/// rather than inside the Ai module because it depends on <see cref="SearchAggregator"/>, which itself fans
/// out across every module — the Ai module must not gain a direct dependency on every other module just for
/// this one capability (AGENTS.md rule 7).
/// </summary>
public sealed class WorkspaceQaService(SearchAggregator aggregator, AiServiceContext aiCtx, IAiRequestStore requests, IAiCompletionProvider provider)
{
    private const int MaxKeywords = 5;
    private const int MaxHitsPerKeyword = 6;
    private const int MaxContextItems = 10;

    private static readonly string[] StopWords =
    [
        "the", "and", "for", "with", "that", "this", "what", "when", "where", "which", "who", "how",
        "are", "was", "were", "does", "did", "has", "have", "had", "our", "your", "their", "about",
    ];

    public async Task<AiAskDto> AskAsync(string? question, CancellationToken ct)
    {
        var trimmedQuestion = (question ?? string.Empty).Trim();
        if (trimmedQuestion.Length == 0)
        {
            throw new Planvexa.BuildingBlocks.Exceptions.ValidationAppException("A question is required.");
        }

        var workspace = aiCtx.WorkspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            throw new Planvexa.BuildingBlocks.Exceptions.ForbiddenException("An X-Workspace header identifying the target workspace is required.");
        }

        var role = (await aiCtx.Access.GetAccessAsync(workspace.WorkspaceId, aiCtx.CurrentUser.UserId, ct))?.Role;
        AiAuthorizer.EnsureUse(role);

        // Retrieval: only ever through the already permission-filtered search fan-out, never a direct
        // table read. Multiple keyword calls (rather than the raw question) because every provider behind
        // the aggregator matches with a literal substring Contains, which a full sentence rarely hits.
        var keywords = ExtractKeywords(trimmedQuestion);
        var hits = new List<SearchHit>();
        var seen = new HashSet<(string Type, Guid Id)>();
        foreach (var keyword in keywords)
        {
            foreach (var hit in await aggregator.SearchAsync(keyword, MaxHitsPerKeyword, ct))
            {
                if (seen.Add((hit.Type, hit.Id)) && hits.Count < MaxContextItems)
                {
                    hits.Add(hit);
                }
            }
        }

        var context = hits
            .Select((h, i) => $"[{i + 1}] {h.Type}: {h.Title}" + (h.Subtitle is null ? string.Empty : $" — {h.Subtitle}"))
            .ToList();

        var prompt = new AiPrompt(AiTaskKind.WorkspaceQna, trimmedQuestion, null, context);
        var completion = await provider.CompleteAsync(prompt, ct);

        var request = Planvexa.Modules.Ai.Domain.AiRequest.Record(
            aiCtx.Ids.NewId(), workspace.WorkspaceId, aiCtx.CurrentUser.UserId,
            requestKey: $"ask:{workspace.WorkspaceId}:{Guid.CreateVersion7()}", // Q&A answers are not idempotency-replayed: each question is its own event.
            AiTaskKind.WorkspaceQna, workspace.WorkspaceId, completion.TokensEstimated, completion.Text, aiCtx.Clock.UtcNow,
            completion.RedactedCount, string.Join(',', completion.RedactedTypes ?? []));
        requests.Add(request);
        aiCtx.Audit.Write("ai.ask", "AiRequest", request.Id, new { QuestionLength = trimmedQuestion.Length, SourceCount = hits.Count });
        await aiCtx.UnitOfWork.SaveChangesAsync(ct);

        return new AiAskDto(completion.Text, hits, completion.TokensEstimated);
    }

    private static IReadOnlyList<string> ExtractKeywords(string question)
        => question
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
            .Where(w => w.Length > 2 && !StopWords.Contains(w.ToLowerInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxKeywords)
            .ToList();
}
