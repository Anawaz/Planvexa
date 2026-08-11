namespace Planvexa.Modules.Clips.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Files;
using Planvexa.Modules.Clips.Authorization;
using Planvexa.Modules.Clips.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Manages workspace clips: upload (both browser-recorded-then-uploaded and pre-recorded file uploads use
/// this same path — the backend only ever sees a finished file, exactly like
/// WorkManagement's AttachmentService/Chat's ChatAttachmentService), metadata, download, and the same
/// linked-resource privacy inheritance pattern as WhiteboardService (see Clip's class doc comment).
/// </summary>
public sealed class ClipService(ClipServiceContext ctx, IClipStore clips)
    : ClipServiceBase(ctx)
{
    /// <summary>Clips are large video/audio uploads (unlike Chat's 25 MB attachment cap) — no
    /// chunked/resumable upload here (ponytail: the browser produces one blob via MediaRecorder
    /// or a file picker; a resumable protocol is real added complexity for a limit this generous).</summary>
    public const long MaxClipBytes = 1024L * 1024 * 1024;

    public async Task<IReadOnlyList<ClipDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = await RoleAsync(workspaceId, ct);
        ClipsAuthorizer.EnsureRead(role);

        var list = await clips.ListByWorkspaceAsync(workspaceId, ct);
        var result = new List<ClipDto>(list.Count);
        foreach (var clip in list)
        {
            if (await CanAccessAsync(clip, role, ct))
            {
                result.Add(ToDto(clip));
            }
        }

        return result;
    }

    public async Task<ClipDto> GetAsync(Guid id, CancellationToken ct)
    {
        var (clip, _) = await LoadForReadAsync(id, ct);
        return ToDto(clip);
    }

    public async Task<ClipDto> UploadAsync(
        string title, string? description, bool isPrivate, string? linkedResourceType, Guid? linkedResourceId,
        double? durationSeconds, string? fileName, string? contentType, long sizeBytes, Stream content, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = await RoleAsync(workspaceId, ct);
        ClipsAuthorizer.EnsureEdit(role);

        if (sizeBytes <= 0)
        {
            throw new ValidationAppException("The uploaded clip is empty.");
        }

        if (sizeBytes > MaxClipBytes)
        {
            throw new ValidationAppException($"Clips are limited to {MaxClipBytes / (1024 * 1024)} MB.");
        }

        var id = NewId();
        var safeName = SanitizeFileName(fileName);
        var storagePath = $"workspaces/{workspaceId}/clips/{id}/{safeName}";
        var safeContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        var validatedContent = await FileContentValidator.ValidateAsync(content, safeName, safeContentType, ct);
        await Ctx.MalwareScanner.EnsureCleanAsync(validatedContent, ct);
        await Ctx.FileStorage.SaveAsync(storagePath, validatedContent, ct);

        Clip clip;
        if (linkedResourceType is { } linkedType)
        {
            if (linkedResourceId is not { } linkedId)
            {
                throw new ValidationAppException("linkedResourceId is required when linkedResourceType is set.");
            }

            if (!await Ctx.LinkedResources.CanViewAsync(workspaceId, UserId, linkedType, linkedId, ct))
            {
                throw new ForbiddenException("You do not have access to the resource this clip would be linked to.");
            }

            clip = Clip.CreateLinked(id, workspaceId, title, description, linkedType, linkedId, UserId, storagePath, safeContentType, sizeBytes, durationSeconds, Now);
        }
        else
        {
            clip = Clip.Create(id, workspaceId, title, description, isPrivate, UserId, storagePath, safeContentType, sizeBytes, durationSeconds, Now);
        }

        clips.Add(clip);
        Audit("clips.clip_uploaded", "Clip", clip.Id, new { clip.Title, clip.IsPrivate, sizeBytes, linkedResourceType, linkedResourceId });
        await SaveAsync(ct);
        return ToDto(clip);
    }

    public async Task<(ClipDto Clip, Stream Content)> DownloadAsync(Guid id, CancellationToken ct)
    {
        var (clip, _) = await LoadForReadAsync(id, ct);
        return (ToDto(clip), await Ctx.FileStorage.OpenReadAsync(clip.StoragePath, ct));
    }

    public async Task<ClipDto> UpdateAsync(Guid id, UpdateClipCommand command, CancellationToken ct)
    {
        var (clip, _) = await LoadForManageAsync(id, ct);
        clip.UpdateDetails(command.Title, command.Description, command.IsPrivate, Now);
        Audit("clips.clip_updated", "Clip", clip.Id, new { command.Title, command.IsPrivate });
        await SaveAsync(ct);
        return ToDto(clip);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var (clip, _) = await LoadForManageAsync(id, ct);
        clips.Remove(clip);
        Audit("clips.clip_deleted", "Clip", clip.Id);
        await SaveAsync(ct);

        // Best effort: the row is the source of truth, an orphaned blob is harmless (mirrors
        // ChatAttachmentService.DeleteAsync's exact rationale).
        try
        {
            await Ctx.FileStorage.DeleteAsync(clip.StoragePath, ct);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Effective read access: identical shape to WhiteboardService.CanAccessAsync — the structural
    /// check (private-to-owner / workspace floor) ANDed with the linked resource's ACL when linked. Used by
    /// every read path (ListAsync, LoadForReadAsync, ClipCommentService, ClipTranscriptService,
    /// ClipSearchProvider) so browsing, direct GET, comments, transcription and search all agree.</summary>
    internal async Task<bool> CanAccessAsync(Clip clip, WorkspaceRole? role, CancellationToken ct)
    {
        if (!clip.CanBeViewedBy(UserId))
        {
            return false;
        }

        if (clip.LinkedResourceType is null || clip.LinkedResourceId is null)
        {
            return true;
        }

        return await Ctx.LinkedResources.CanViewAsync(clip.WorkspaceId, UserId, clip.LinkedResourceType, clip.LinkedResourceId.Value, ct);
    }

    internal async Task<(Clip Clip, WorkspaceRole? Role)> LoadForReadAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = await RoleAsync(workspaceId, ct);
        ClipsAuthorizer.EnsureRead(role);

        var clip = await clips.FindAsync(id, ct) ?? throw new NotFoundException("Clip not found.");
        if (clip.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Clip not found.");
        }

        if (!await CanAccessAsync(clip, role, ct))
        {
            throw new ForbiddenException("You do not have access to this clip.");
        }

        return (clip, role);
    }

    internal async Task<(Clip Clip, WorkspaceRole? Role)> LoadForManageAsync(Guid id, CancellationToken ct)
    {
        var (clip, role) = await LoadForReadAsync(id, ct);
        ClipsAuthorizer.EnsureEdit(role);
        if (clip.IsPrivate && clip.OwnerUserId != UserId && !ClipsAuthorizer.CanManage(role))
        {
            throw new ForbiddenException("Only the clip owner or a workspace administrator can modify this private clip.");
        }

        return (clip, role);
    }

    private static string SanitizeFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Trim();
        var separator = name.LastIndexOfAny(['/', '\\', ':']);
        if (separator >= 0)
        {
            name = name[(separator + 1)..];
        }

        name = string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim('.', ' ');

        if (name.Length > 260)
        {
            name = name[^260..];
        }

        return name.Length == 0 ? "clip" : name;
    }

    private static ClipDto ToDto(Clip c)
        => new(c.Id, c.Title, c.Description, c.IsPrivate, c.OwnerUserId, c.LinkedResourceType, c.LinkedResourceId,
            c.ContentType, c.SizeBytes, c.DurationSeconds, c.Status, c.CreatedAtUtc, c.UpdatedAtUtc);
}
