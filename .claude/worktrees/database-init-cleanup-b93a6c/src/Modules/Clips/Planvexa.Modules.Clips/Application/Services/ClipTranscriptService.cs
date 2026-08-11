namespace Planvexa.Modules.Clips.Application.Services;

using System.Text.Json;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Clips.Authorization;
using Planvexa.Modules.Clips.Domain;

/// <summary>
/// Clip transcription. See <c>IClipTranscriber</c>'s doc comment for what is and
/// isn't implemented: a real HTTP call to an OpenAI/Whisper-compatible <c>/audio/transcriptions</c>
/// endpoint when the workspace has one configured, an honest <see cref="ClipTranscriptStatus.Unavailable"/>
/// row (never a faked transcript) when it doesn't.
/// </summary>
public sealed class ClipTranscriptService(ClipServiceContext ctx, IClipTranscriptStore transcripts, ClipService clipService)
    : ClipServiceBase(ctx)
{
    public async Task<ClipTranscriptDto?> GetAsync(Guid clipId, CancellationToken ct)
    {
        var (clip, _) = await clipService.LoadForReadAsync(clipId, ct);
        var transcript = await transcripts.FindByClipAsync(clip.WorkspaceId, clip.Id, ct);
        return transcript is null ? null : ToDto(transcript);
    }

    public async Task<ClipTranscriptDto> RequestAsync(Guid clipId, CancellationToken ct)
    {
        var (clip, role) = await clipService.LoadForReadAsync(clipId, ct);
        ClipsAuthorizer.EnsureEdit(role);

        var existing = await transcripts.FindByClipAsync(clip.WorkspaceId, clip.Id, ct);

        await using var audio = await Ctx.FileStorage.OpenReadAsync(clip.StoragePath, ct);
        ClipTranscriptionResultLocal? result;
        try
        {
            var transcribed = await Ctx.Transcriber.TranscribeAsync(clip.WorkspaceId, audio, clip.Title, clip.ContentType, ct);
            result = transcribed is null ? null : new ClipTranscriptionResultLocal(transcribed.Text, transcribed.Segments);
        }
        catch (ExternalServiceException)
        {
            var failed = existing ?? ClipTranscript.CreatePending(NewId(), clip.WorkspaceId, clip.Id, Now);
            if (existing is null)
            {
                transcripts.Add(failed);
            }

            failed.MarkFailed(Now);
            Audit("clips.transcription_failed", "Clip", clip.Id);
            await SaveAsync(ct);
            throw;
        }

        if (result is null)
        {
            // No usable provider configured — persist the honest "unavailable" state rather than leaving
            // no row (so GetAsync can tell "never asked" apart from "asked, nothing available").
            var unavailable = existing ?? ClipTranscript.CreateUnavailable(NewId(), clip.WorkspaceId, clip.Id, Now);
            if (existing is null)
            {
                transcripts.Add(unavailable);
            }
            else
            {
                unavailable.MarkUnavailable(Now);
            }

            Audit("clips.transcription_unavailable", "Clip", clip.Id);
            await SaveAsync(ct);
            return ToDto(unavailable);
        }

        var transcript = existing ?? ClipTranscript.CreatePending(NewId(), clip.WorkspaceId, clip.Id, Now);
        if (existing is null)
        {
            transcripts.Add(transcript);
        }

        var segmentsJson = result.Segments.Count == 0 ? null : JsonSerializer.Serialize(result.Segments);
        transcript.MarkReady(result.Text, segmentsJson, Now);
        Audit("clips.transcription_ready", "Clip", clip.Id, new { textLength = result.Text.Length, segmentCount = result.Segments.Count });
        await SaveAsync(ct);
        return ToDto(transcript);
    }

    private static ClipTranscriptDto ToDto(ClipTranscript t)
    {
        IReadOnlyList<ClipTranscriptSegmentDto>? segments = null;
        if (!string.IsNullOrWhiteSpace(t.SegmentsJson))
        {
            try
            {
                segments = JsonSerializer.Deserialize<List<ClipTranscriptSegmentDto>>(t.SegmentsJson);
            }
            catch (JsonException)
            {
                segments = null;
            }
        }

        return new ClipTranscriptDto(t.Status, t.Text, segments, t.UpdatedAtUtc);
    }

    private sealed record ClipTranscriptionResultLocal(string Text, IReadOnlyList<Planvexa.SharedContracts.Ai.ClipTranscriptSegment> Segments);
}
