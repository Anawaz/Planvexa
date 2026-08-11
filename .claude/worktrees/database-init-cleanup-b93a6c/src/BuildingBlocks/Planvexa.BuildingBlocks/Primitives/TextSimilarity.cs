namespace Planvexa.BuildingBlocks.Primitives;

/// <summary>
/// Pure, deterministic text-similarity scoring (Jaccard token overlap). Lives in the shared kernel (no
/// external module deps, AGENTS.md rule 7) because it is used both across modules that must not depend on
/// each other directly — e.g. WorkManagement's duplicate-task detection and dependency-suggestion
/// heuristics, and the Ai module's own extractive fallback — without introducing a module dependency
/// edge for what is just string math.
/// </summary>
public static class TextSimilarity
{
    /// <summary>
    /// Jaccard similarity of the two texts' lowercased word-token sets: |intersection| / |union|, in
    /// [0, 1]. Two empty/blank texts score 0 (no evidence of similarity, not "identical"). Cheap and
    /// deterministic — good enough for a "possible duplicate" hint, not a semantic embedding.
    /// </summary>
    public static double Jaccard(string? a, string? b)
    {
        var tokensA = Tokenize(a);
        var tokensB = Tokenize(b);
        if (tokensA.Count == 0 || tokensB.Count == 0)
        {
            return 0d;
        }

        var intersection = tokensA.Intersect(tokensB).Count();
        var union = tokensA.Union(tokensB).Count();
        return union == 0 ? 0d : (double)intersection / union;
    }

    private static HashSet<string> Tokenize(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? new HashSet<string>()
            : text
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant())
                .Where(w => w.Length > 2)
                .ToHashSet();
}
