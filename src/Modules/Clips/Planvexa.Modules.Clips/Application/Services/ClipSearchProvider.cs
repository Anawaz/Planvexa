namespace Planvexa.Modules.Clips.Application.Services;

using Planvexa.Modules.Clips.Authorization;
using Planvexa.Modules.Clips.Domain;
using Planvexa.SharedContracts.Search;

/// <summary>
/// Cross-module search fan-out, extended for "searchable transcripts": matches
/// a clip's title or its Ready transcript's text. Every result is filtered through
/// <see cref="ClipService.CanAccessAsync"/> — the exact same rule GetAsync/ListAsync/comments/transcription
/// apply — before it is returned, so a private or ungranted-linked clip's transcript can never leak through
/// search (the recurring bug class a prior audit of this roadmap flagged).
/// </summary>
public sealed class ClipSearchProvider(ClipServiceContext ctx, IClipStore clips, IClipTranscriptStore transcripts, ClipService clipService)
    : ClipServiceBase(ctx), ISearchProvider
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int limit, CancellationToken cancellationToken = default)
    {
        var workspace = Ctx.WorkspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            return [];
        }

        var role = await RoleAsync(workspace.WorkspaceId, cancellationToken);
        if (!ClipsAuthorizer.CanRead(role))
        {
            return [];
        }

        var list = await clips.ListByWorkspaceAsync(workspace.WorkspaceId, cancellationToken);
        var transcriptsByClip = await transcripts.ListReadyByWorkspaceAsync(workspace.WorkspaceId, cancellationToken);

        var hits = new List<SearchHit>();
        foreach (var clip in list)
        {
            if (hits.Count >= limit)
            {
                break;
            }

            transcriptsByClip.TryGetValue(clip.Id, out var transcript);
            var transcriptText = transcript?.Status == ClipTranscriptStatus.Ready ? transcript.Text : null;

            var titleMatch = clip.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
            var transcriptMatch = transcriptText is not null && transcriptText.Contains(term, StringComparison.OrdinalIgnoreCase);
            if (!titleMatch && !transcriptMatch)
            {
                continue;
            }

            if (!await clipService.CanAccessAsync(clip, role, cancellationToken))
            {
                continue;
            }

            var snippet = titleMatch
                ? (clip.IsPrivate ? "Private clip" : "Clip")
                : Snippet(transcriptText!, term);
            hits.Add(new SearchHit("Clip", clip.Id, clip.Title, snippet, null));
        }

        return hits;
    }

    private static string Snippet(string text, string term)
    {
        var index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        var start = index < 0 ? 0 : Math.Max(0, index - 40);
        var length = Math.Min(140, text.Length - start);
        var snippet = text.Substring(start, length).Replace('\n', ' ').Trim();
        return start > 0 ? $"…{snippet}" : snippet;
    }
}
