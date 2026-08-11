namespace Planvexa.Modules.Ai.Domain;

using System.Text.RegularExpressions;

/// <summary>A workspace's redaction configuration (mirrors the toggles + custom patterns on <see cref="AiProviderSettings"/>).</summary>
public sealed record RedactionOptions(bool RedactEmails, bool RedactApiKeys, bool RedactCreditCards, IReadOnlyList<string> CustomPatterns)
{
    /// <summary>The default, most-conservative configuration (every built-in pattern on, no custom patterns).</summary>
    public static RedactionOptions Default { get; } = new(true, true, true, []);
}

/// <summary>
/// A redacted text plus what was found — never the matched value itself, only a count and the pattern
/// type(s), so the <see cref="AiRequest"/> audit record can say "2 emails redacted" without logging the
/// email address.
/// </summary>
public sealed record RedactionResult(string Text, int RedactedCount, IReadOnlyList<string> RedactedTypes);

/// <summary>
/// A configurable redaction pass applied to content before it is sent to an external
/// (real) AI provider — never needed for the offline <see cref="ExtractiveAi"/> fallback, which produces
/// its result on the server and never makes an outbound call. Pure and deterministic: no I/O, so it is
/// safe to run on every outbound request without added latency risk beyond the regex match itself (bounded
/// by <see cref="MatchTimeout"/>, since custom per-workspace patterns are attacker-influenceable input).
/// </summary>
public static class Redactor
{
    /// <summary>Guards against a pathological custom regex (ReDoS) hanging the outbound request.</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);

    private static readonly Regex EmailPattern = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, MatchTimeout);

    /// <summary>Shaped like a provider API key: a short scheme prefix followed by a long token, or a bare
    /// long alphanumeric/base64-ish run (e.g. a raw key or JWT segment).</summary>
    private static readonly Regex ApiKeyPattern = new(
        @"\b(?:sk|pk|api|key|token|bearer)[-_][A-Za-z0-9]{12,}\b|\b[A-Za-z0-9_-]{32,}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, MatchTimeout);

    /// <summary>13-19 digits, optionally grouped by spaces/dashes in 4s — covers the common card-number shapes.</summary>
    private static readonly Regex CreditCardPattern = new(
        @"\b(?:\d[ -]?){12,18}\d\b",
        RegexOptions.Compiled, MatchTimeout);

    public static RedactionResult Redact(string? text, RedactionOptions options)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new RedactionResult(text ?? string.Empty, 0, []);
        }

        var result = text;
        var count = 0;
        var types = new List<string>();

        if (options.RedactEmails)
        {
            (result, var n) = Apply(result, EmailPattern, "[REDACTED_EMAIL]");
            if (n > 0)
            {
                count += n;
                types.Add("email");
            }
        }

        if (options.RedactCreditCards)
        {
            (result, var n) = Apply(result, CreditCardPattern, "[REDACTED_CARD]");
            if (n > 0)
            {
                count += n;
                types.Add("credit_card");
            }
        }

        // API-key-shaped tokens last: the broad "long token" fallback would otherwise also swallow an
        // already-redacted placeholder or match inside an email/card that was just replaced.
        if (options.RedactApiKeys)
        {
            (result, var n) = Apply(result, ApiKeyPattern, "[REDACTED_KEY]");
            if (n > 0)
            {
                count += n;
                types.Add("api_key");
            }
        }

        foreach (var pattern in options.CustomPatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            Regex custom;
            try
            {
                custom = new Regex(pattern, RegexOptions.None, MatchTimeout);
            }
            catch (ArgumentException)
            {
                // An invalid workspace-supplied pattern must never break an AI call — skip it silently
                // (validated at settings-save time; this is defense in depth).
                continue;
            }

            (result, var n) = Apply(result, custom, "[REDACTED]");
            if (n > 0)
            {
                count += n;
                types.Add("custom");
            }
        }

        return new RedactionResult(result, count, types);
    }

    /// <summary>Swallows a pathological-pattern timeout by leaving the text as-is for that pass — a
    /// misbehaving regex must never break (or hang) an AI call.</summary>
    private static (string Text, int Count) Apply(string text, Regex pattern, string replacement)
    {
        var count = 0;
        try
        {
            var replaced = pattern.Replace(text, _ =>
            {
                count++;
                return replacement;
            });
            return (replaced, count);
        }
        catch (RegexMatchTimeoutException)
        {
            return (text, 0);
        }
    }
}
