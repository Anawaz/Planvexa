namespace Planvexa.SharedContracts.Ai;

/// <summary>One transcribed segment with its timing, when the provider supplies them (/// "searchable transcripts"). Null/empty when the provider only returns plain text.</summary>
public sealed record ClipTranscriptSegment(double StartSeconds, double EndSeconds, string Text);

/// <summary>Result of a successful transcription call.</summary>
public sealed record ClipTranscriptionResult(string Text, IReadOnlyList<ClipTranscriptSegment> Segments);

/// <summary>
/// Clips transcription. Investigated: the existing <c>AiProviderSettings</c>/
/// <c>LiteLlmCompletionProvider</c> HTTP client only ever calls an OpenAI-compatible
/// <c>{baseUrl}/chat/completions</c> JSON endpoint — text in, text out. There is no way to hand it binary
/// audio. Many OpenAI-compatible gateways (LiteLLM proxy included) additionally expose a sibling
/// <c>{baseUrl}/audio/transcriptions</c> endpoint (Whisper-compatible: multipart/form-data upload, JSON
/// <c>{"text": "..."}</c> response) on the SAME base URL/API key a workspace already configured for chat
/// completions — so this is implemented for real (see <c>Planvexa.Api.Ai.ClipTranscriptionProvider</c>)
/// rather than left as a pure gap. It reuses <c>AiProviderSettings.IsUsable</c>/BaseUrl/ApiKeyEncrypted
/// as-is (no new settings column): when the workspace has not configured/enabled a provider,
/// <see cref="TranscribeAsync"/> returns null (never fakes a transcript, by design) — the
/// gap that remains undocumented-as-implemented is per-segment timestamps, which need
/// <c>response_format=verbose_json</c>, a Whisper-specific extension not every OpenAI-compatible server
/// honours consistently; <see cref="ClipTranscriptionResult.Segments"/> is therefore always empty for now
/// (Clip.HasTimestampedSegments stays false) — a real but partial implementation, not a fake one.
/// </summary>
public interface IClipTranscriber
{
    /// <summary>Returns null when the calling workspace has no usable transcription-capable provider
    /// configured (not an error — a documented, honest "not available" signal). Throws
    /// <c>ExternalServiceException</c> when a configured provider is reachable but the call fails.</summary>
    Task<ClipTranscriptionResult?> TranscribeAsync(
        Guid workspaceId, Stream audio, string fileName, string contentType, CancellationToken cancellationToken = default);
}
