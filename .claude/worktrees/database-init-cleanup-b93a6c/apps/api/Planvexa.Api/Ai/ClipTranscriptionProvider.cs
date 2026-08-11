namespace Planvexa.Api.Ai;

using System.Net.Http.Headers;
using System.Text.Json;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Ai.Application;
using Planvexa.SharedContracts.Ai;

/// <summary>
/// Clips transcription. See <see cref="IClipTranscriber"/>'s doc comment for the
/// investigation: <see cref="LiteLlmCompletionProvider"/>'s existing HTTP client only ever calls
/// <c>{baseUrl}/chat/completions</c> with JSON — no way to hand it binary audio. This class reuses the
/// SAME per-workspace <c>AiProviderSettings</c> (BaseUrl/ApiKeyEncrypted/IsUsable, no new settings column)
/// but calls the sibling Whisper-compatible <c>{baseUrl}/audio/transcriptions</c> endpoint instead, which
/// LiteLLM's proxy (and most OpenAI-compatible gateways) expose alongside chat completions on the same
/// base URL. multipart/form-data upload, minimal <c>response_format=json</c> parse (just
/// <c>{"text": "..."}</c>) — deliberately NOT requesting <c>verbose_json</c> segments, since that's a
/// Whisper-specific extension not every gateway honours consistently; <see cref="ClipTranscriptionResult.Segments"/>
/// is therefore always empty (a real but partial implementation, not a fake one — see the interface's doc
/// comment).
/// </summary>
public sealed class ClipTranscriptionProvider(
    IHttpClientFactory httpClientFactory,
    IAiProviderSettingsStore settingsStore,
    IAiSecretProtector protector,
    IWorkspaceContextAccessor workspaceAccessor)
    : IClipTranscriber
{
    public const string ClientName = "clip-transcription";

    /// <summary>Whisper-compatible endpoints commonly default to this model name when none is configured;
    /// AiProviderSettings has no separate transcription-model field (ponytail: one more per-workspace
    /// setting for a single hardcoded default isn't worth it yet — add if a workspace's gateway needs a
    /// different model name).</summary>
    private const string DefaultModel = "whisper-1";

    public async Task<ClipTranscriptionResult?> TranscribeAsync(
        Guid workspaceId, Stream audio, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        // Only honour the CALLER's own workspace context, never the workspaceId argument blindly — mirrors
        // LiteLlmCompletionProvider.CompleteAsync's exact guard (defense in depth: even though callers only
        // ever pass the ambient workspace's own id today, this keeps that invariant true structurally).
        var current = workspaceAccessor.Current;
        if (!current.HasWorkspace || current.WorkspaceId != workspaceId)
        {
            return null;
        }

        var settings = await settingsStore.FindAsync(workspaceId, cancellationToken);
        if (settings is not { IsUsable: true })
        {
            return null;
        }

        var apiKey = protector.Unprotect(settings.ApiKeyEncrypted);
        var client = httpClientFactory.CreateClient(ClientName);

        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(audio);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var parsed) ? parsed : new MediaTypeHeaderValue("application/octet-stream");
        content.Add(streamContent, "file", string.IsNullOrWhiteSpace(fileName) ? "clip" : fileName);
        content.Add(new StringContent(DefaultModel), "model");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl.TrimEnd('/')}/audio/transcriptions")
        {
            Content = content,
        };
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new ExternalServiceException($"Could not reach the transcription provider at {settings.BaseUrl}: {ex.Message}");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ExternalServiceException(
                    $"The transcription provider returned {(int)response.StatusCode} {response.StatusCode}: {Excerpt(body)}");
            }

            try
            {
                using var json = JsonDocument.Parse(body);
                var text = json.RootElement.GetProperty("text").GetString() ?? string.Empty;
                return new ClipTranscriptionResult(text.Trim(), []);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new ExternalServiceException($"The transcription provider returned an unexpected response: {Excerpt(body)}");
            }
        }
    }

    private static string Excerpt(string body) => body.Length <= 300 ? body : body[..300] + "…";
}
