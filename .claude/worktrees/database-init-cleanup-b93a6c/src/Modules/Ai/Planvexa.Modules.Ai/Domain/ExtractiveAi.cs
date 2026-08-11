namespace Planvexa.Modules.Ai.Domain;

using System.Text;
using Planvexa.SharedContracts.Ai;

/// <summary>
/// Pure, deterministic AI logic used by the default (offline) completion provider. No I/O, no external
/// calls, no randomness — heavily unit-tested. Produces bounded, structured text for each task kind from
/// the already-permission-checked prompt. A real LLM provider can replace this behind
/// <see cref="IAiCompletionProvider"/>; the Ai module and its tests do not depend on which provider is used.
/// </summary>
public static class ExtractiveAi
{
    private const int MaxSummaryChars = 500;
    private const int MaxSubtasks = 6;
    private const int MaxSubtaskChars = 120;

    private static readonly string[] UrgentSignals = { "overdue", "urgent", "asap", "critical", "immediately", "emergency" };
    private static readonly string[] HighSignals = { "important", "high priority", "blocker", "blocking", "deadline" };
    private static readonly string[] LowSignals = { "someday", "low priority", "nice to have", "backlog", "whenever" };

    /// <summary>Produces a completion for the prompt's kind. Deterministic and bounded.</summary>
    public static AiCompletion Complete(AiPrompt prompt) => prompt.Kind switch
    {
        AiTaskKind.Summarize => Summarize(prompt),
        AiTaskKind.GenerateSubtasks => GenerateSubtasks(prompt),
        AiTaskKind.SuggestPriority => SuggestPriority(prompt),
        AiTaskKind.SummarizeComments => SummarizeNotes(prompt, "No comments yet."),
        AiTaskKind.SummarizeDocument => SummarizeDocument(prompt),
        AiTaskKind.SummarizeChat => SummarizeNotes(prompt, "No recent messages."),
        AiTaskKind.RiskDetect => DetectRisk(prompt),
        AiTaskKind.WorkspaceQna => AnswerFromContext(prompt),
        _ => new AiCompletion(string.Empty, 1),
    };

    /// <summary>
    /// Offline fallback for workspace Q&amp;A: no LLM, so no synthesis — just an extractive listing of the
    /// already permission-filtered search context items the caller (WorkspaceQaService) assembled. Never
    /// invents an answer beyond what is in <see cref="AiPrompt.Context"/>, which is exactly the same
    /// "answer only from what search would have returned for this user" guarantee the real-provider path
    /// gives via its system prompt.
    /// </summary>
    private static AiCompletion AnswerFromContext(AiPrompt prompt)
    {
        var text = prompt.Context.Count == 0
            ? "I could not find anything about that in the material I have access to."
            : Truncate("Closest matches: " + string.Join("; ", prompt.Context), MaxSummaryChars);
        return new AiCompletion(text, EstimateTokens(prompt.Title, null, text));
    }

    /// <summary>Shared "join and truncate the context lines" shape used by comment and chat summaries:
    /// there is no single description to lead with, only a list of notes/messages.</summary>
    private static AiCompletion SummarizeNotes(AiPrompt prompt, string emptyText)
    {
        var text = prompt.Context.Count == 0
            ? emptyText
            : Truncate(string.Join(' ', prompt.Context.Select(c => c.Trim().TrimEnd('.') + '.')), MaxSummaryChars);
        return new AiCompletion(text, EstimateTokens(prompt.Title, prompt.Description, text));
    }

    private static AiCompletion SummarizeDocument(AiPrompt prompt)
    {
        var lead = FirstSentences(prompt.Description, 3);
        var text = Truncate(string.IsNullOrWhiteSpace(lead) ? $"{prompt.Title.Trim()}: no content yet." : lead, MaxSummaryChars);
        return new AiCompletion(text, EstimateTokens(prompt.Title, prompt.Description, text));
    }

    /// <summary>
    /// Deterministic risk heuristic: overdue and incomplete is Urgent risk; blocked (an unfinished
    /// blocking dependency, passed in via <see cref="AiPrompt.Context"/> as the literal "blocked" signal —
    /// see <c>AiAssistService.BuildPrompt</c>) or due within 2 days is At-risk; otherwise On-track. No I/O,
    /// mirrors <see cref="SuggestPriority"/>'s signal-scanning shape.
    /// </summary>
    private static AiCompletion DetectRisk(AiPrompt prompt)
    {
        var signals = prompt.Context.Select(c => c.ToLowerInvariant()).ToHashSet();
        string status;
        string reason;
        if (signals.Contains("overdue"))
        {
            status = "AtRisk";
            reason = "The task is overdue.";
        }
        else if (signals.Contains("blocked"))
        {
            status = "AtRisk";
            reason = "The task is blocked by an unfinished dependency.";
        }
        else if (signals.Contains("due-soon"))
        {
            status = "AtRisk";
            reason = "The task is due within two days.";
        }
        else
        {
            status = "OnTrack";
            reason = "No overdue, blocked, or imminent-due-date signals were found.";
        }

        var text = $"{status}|{reason}";
        return new AiCompletion(text, EstimateTokens(prompt.Title, prompt.Description, text));
    }

    private static AiCompletion Summarize(AiPrompt prompt)
    {
        var builder = new StringBuilder();
        builder.Append(prompt.Title.Trim().TrimEnd('.'));
        builder.Append('.');

        var lead = FirstSentences(prompt.Description, 2);
        if (!string.IsNullOrWhiteSpace(lead))
        {
            builder.Append(' ').Append(lead);
        }

        if (prompt.Context.Count > 0)
        {
            builder.Append(' ').Append("Includes ").Append(prompt.Context.Count).Append(prompt.Context.Count == 1 ? " related note." : " related notes.");
        }

        var summary = Truncate(builder.ToString().Trim(), MaxSummaryChars);
        return new AiCompletion(summary, EstimateTokens(prompt.Title, prompt.Description, summary));
    }

    private static AiCompletion GenerateSubtasks(AiPrompt prompt)
    {
        var candidates = prompt.Context.Count > 0
            ? prompt.Context
            : SplitSentences(prompt.Description);

        var titles = candidates
            .Select(c => Truncate(c.Trim().TrimEnd('.'), MaxSubtaskChars))
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxSubtasks)
            .ToList();

        if (titles.Count == 0)
        {
            titles.Add(Truncate($"Break down: {prompt.Title.Trim()}", MaxSubtaskChars));
        }

        var text = string.Join('\n', titles);
        return new AiCompletion(text, EstimateTokens(prompt.Title, prompt.Description, text));
    }

    private static AiCompletion SuggestPriority(AiPrompt prompt)
    {
        var haystack = (prompt.Title + " " + (prompt.Description ?? string.Empty) + " " + string.Join(' ', prompt.Context)).ToLowerInvariant();

        string priority;
        string rationale;
        if (UrgentSignals.Any(haystack.Contains))
        {
            priority = "Urgent";
            rationale = "Signals of time-critical or overdue work were detected.";
        }
        else if (HighSignals.Any(haystack.Contains))
        {
            priority = "High";
            rationale = "The task references importance, blockers, or a deadline.";
        }
        else if (LowSignals.Any(haystack.Contains))
        {
            priority = "Low";
            rationale = "The task reads as low-urgency or backlog work.";
        }
        else
        {
            priority = "Normal";
            rationale = "No strong urgency signals were found.";
        }

        var text = $"{priority}|{rationale}";
        return new AiCompletion(text, EstimateTokens(prompt.Title, prompt.Description, text));
    }

    /// <summary>Rough token estimate: number of whitespace-separated words across inputs (min 1).</summary>
    public static int EstimateTokens(params string?[] inputs)
    {
        var words = inputs
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Sum(s => s!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
        return Math.Max(1, words);
    }

    internal static string FirstSentences(string? text, int count)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sentences = SplitSentences(text);
        return string.Join(' ', sentences.Take(count).Select(s => s.EndsWith('.') ? s : s + "."));
    }

    internal static IReadOnlyList<string> SplitSentences(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        return text
            .Split(new[] { '.', '\n', '\r', '!', '?' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max].TrimEnd();
}
