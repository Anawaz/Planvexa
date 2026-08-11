namespace Planvexa.Api.Ai;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Ai.Application;
using Planvexa.Modules.Ai.Domain;
using Planvexa.SharedContracts.Ai;

/// <summary>
/// Routes AI completions to the calling workspace's own LiteLLM / OpenAI-compatible endpoint, falling
/// back to <see cref="DeterministicAiCompletionProvider"/> when the workspace has not configured one
/// (or has it switched off). A configured provider that fails is surfaced as an error — never silently
/// downgraded, so a broken endpoint is visible instead of quietly producing worse results.
/// </summary>
public sealed class LiteLlmCompletionProvider(
    IHttpClientFactory httpClientFactory,
    IAiProviderSettingsStore settingsStore,
    IAiSecretProtector protector,
    IWorkspaceContextAccessor workspaceAccessor,
    DeterministicAiCompletionProvider fallback)
    : IAiCompletionProvider, IAiProviderProbe
{
    public const string ClientName = "litellm";

    public async Task<AiCompletion> CompleteAsync(AiPrompt prompt, CancellationToken cancellationToken = default)
    {
        var workspace = workspaceAccessor.Current;
        var settings = workspace.HasWorkspace ? await settingsStore.FindAsync(workspace.WorkspaceId, cancellationToken) : null;
        if (settings is not { IsUsable: true })
        {
            return await fallback.CompleteAsync(prompt, cancellationToken);
        }

        var apiKey = protector.Unprotect(settings.ApiKeyEncrypted);

        // Redact sensitive content before it ever leaves the server. Only the real
        // provider path redacts — the offline ExtractiveAi fallback above never makes an outbound call.
        var redactedTitle = Redactor.Redact(prompt.Title, settings.RedactionOptions);
        var redactedDescription = Redactor.Redact(prompt.Description, settings.RedactionOptions);
        var redactedContext = prompt.Context.Select(c => Redactor.Redact(c, settings.RedactionOptions)).ToList();
        var safePrompt = prompt with
        {
            Title = redactedTitle.Text,
            Description = redactedDescription.Text,
            Context = redactedContext.Select(c => c.Text).ToList(),
        };
        var redactedCount = redactedTitle.RedactedCount + redactedDescription.RedactedCount + redactedContext.Sum(c => c.RedactedCount);
        var redactedTypes = redactedTitle.RedactedTypes
            .Concat(redactedDescription.RedactedTypes)
            .Concat(redactedContext.SelectMany(c => c.RedactedTypes))
            .Distinct()
            .ToList();

        var (text, tokens) = await ChatAsync(
            settings.BaseUrl, settings.Model, apiKey, SystemPrompt(prompt.Kind), UserPrompt(safePrompt), cancellationToken);

        var shaped = Shape(prompt.Kind, text);
        return new AiCompletion(
            shaped, tokens ?? ExtractiveAi.EstimateTokens(safePrompt.Title, safePrompt.Description, shaped),
            redactedCount, redactedTypes);
    }

    /// <summary>Minimal live call used by the settings UI's "Test connection".</summary>
    public async Task<string?> TestAsync(string baseUrl, string model, string apiKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await ChatAsync(baseUrl, model, apiKey, "You are a connectivity probe.", "Reply with OK.", cancellationToken);
            return null;
        }
        catch (ExternalServiceException ex)
        {
            return ex.Message;
        }
    }

    private async Task<(string Text, int? Tokens)> ChatAsync(
        string baseUrl, string model, string apiKey, string system, string user, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user },
                },
                temperature = 0.2,
            }),
        };

        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ExternalServiceException($"Could not reach the AI provider at {baseUrl}: {ex.Message}");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new ExternalServiceException(
                    $"The AI provider returned {(int)response.StatusCode} {response.StatusCode}: {Excerpt(body)}");
            }

            try
            {
                using var json = JsonDocument.Parse(body);
                var text = json.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? string.Empty;

                var tokens = json.RootElement.TryGetProperty("usage", out var usage)
                    && usage.TryGetProperty("total_tokens", out var total)
                    && total.TryGetInt32(out var value) && value > 0
                        ? value
                        : (int?)null;

                return (text.Trim(), tokens);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
            {
                throw new ExternalServiceException($"The AI provider returned an unexpected response: {Excerpt(body)}");
            }
        }
    }

    private static string SystemPrompt(AiTaskKind kind) => kind switch
    {
        AiTaskKind.Summarize =>
            "You summarize project tasks. Reply with at most three plain-text sentences. No preamble, no markdown.",
        AiTaskKind.GenerateSubtasks =>
            "You break project tasks into subtasks. Reply with at most six short subtask titles, one per line. "
            + "No numbering, no bullets, no preamble, no markdown.",
        AiTaskKind.SuggestPriority =>
            "You triage project tasks. Choose exactly one priority from: None, Low, Normal, High, Urgent. "
            + "Reply with exactly `Priority|one short sentence of rationale` and nothing else.",
        AiTaskKind.SummarizeComments =>
            "You summarize a discussion thread on a project task. Reply with at most three plain-text sentences. No preamble, no markdown.",
        AiTaskKind.SummarizeDocument =>
            "You summarize a workspace document. Reply with at most three plain-text sentences. No preamble, no markdown.",
        AiTaskKind.SummarizeChat =>
            "You summarize a chat channel's recent messages. Reply with at most three plain-text sentences. No preamble, no markdown.",
        AiTaskKind.RiskDetect =>
            "You assess delivery risk for a project task. Choose exactly one status from: OnTrack, AtRisk. "
            + "Reply with exactly `Status|one short sentence of rationale` and nothing else.",
        AiTaskKind.WorkspaceQna =>
            "You answer a question about a workspace using ONLY the numbered context items given to you. "
            + "If the context does not contain the answer, say you could not find it. Cite item numbers like [1]. "
            + "Reply with at most four plain-text sentences. No markdown.",
        _ => "You assist with project tasks. Reply with plain text only.",
    };

    private static string UserPrompt(AiPrompt prompt)
    {
        var lines = new List<string> { $"Title: {prompt.Title}" };
        if (!string.IsNullOrWhiteSpace(prompt.Description))
        {
            lines.Add($"Description: {prompt.Description}");
        }

        if (prompt.Context.Count > 0)
        {
            lines.Add("Context:");
            lines.AddRange(prompt.Context.Select(c => $"- {c}"));
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Models add bullets and numbering however hard you ask them not to, and AiAssistService parses
    /// subtask output line-by-line — so strip list markers here.
    /// </summary>
    private static string Shape(AiTaskKind kind, string text) => kind switch
    {
        AiTaskKind.GenerateSubtasks => string.Join('\n', text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart('-', '*', '•', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ')', ' ').Trim())
            .Where(line => line.Length > 0)
            .Take(6)),
        _ => text,
    };

    private static string Excerpt(string body)
        => body.Length <= 300 ? body : body[..300] + "…";
}
