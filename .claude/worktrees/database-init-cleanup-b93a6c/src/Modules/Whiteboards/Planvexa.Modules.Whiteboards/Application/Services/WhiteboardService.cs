namespace Planvexa.Modules.Whiteboards.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Whiteboards.Authorization;
using Planvexa.Modules.Whiteboards.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>Manages workspace whiteboards: metadata CRUD, linked-resource privacy inheritance, and the
/// collaboration-room authorization check the Hocuspocus server relies on (mirrors DocumentService).</summary>
public sealed class WhiteboardService(
    WhiteboardServiceContext ctx, IWhiteboardStore whiteboards, IWhiteboardTemplateStore templates, IWhiteboardCollabStateStore collabState)
    : WhiteboardServiceBase(ctx)
{
    public async Task<IReadOnlyList<WhiteboardDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = await RoleAsync(workspaceId, ct);
        WhiteboardsAuthorizer.EnsureRead(role);

        var list = await whiteboards.ListByWorkspaceAsync(workspaceId, ct);
        var result = new List<WhiteboardDto>(list.Count);
        foreach (var wb in list)
        {
            if (await CanAccessAsync(wb, role, ct))
            {
                result.Add(ToDto(wb));
            }
        }

        return result;
    }

    public async Task<WhiteboardDto> GetAsync(Guid id, CancellationToken ct)
    {
        var (wb, _) = await LoadForReadAsync(id, ct);
        return ToDto(wb);
    }

    public async Task<WhiteboardDto> CreateAsync(CreateWhiteboardCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = await RoleAsync(workspaceId, ct);
        WhiteboardsAuthorizer.EnsureEdit(role);

        Whiteboard wb;
        if (command.LinkedResourceType is { } linkedType)
        {
            if (command.LinkedResourceId is not { } linkedId)
            {
                throw new ValidationAppException("linkedResourceId is required when linkedResourceType is set.");
            }

            // The caller must already be able to view the resource they're linking to — else linking would
            // let them create a whiteboard that exposes a resource they cannot themselves see (same rule
            // ChatChannelService.CreateLinkedAsync applies).
            if (!await Ctx.LinkedResources.CanViewAsync(workspaceId, UserId, linkedType, linkedId, ct))
            {
                throw new ForbiddenException("You do not have access to the resource this whiteboard would be linked to.");
            }

            wb = Whiteboard.CreateLinked(NewId(), workspaceId, command.Name, linkedType, linkedId, UserId, Now);
        }
        else
        {
            wb = Whiteboard.Create(NewId(), workspaceId, command.Name, command.IsPrivate, UserId, Now);
        }

        whiteboards.Add(wb);

        if (command.TemplateId is { } templateId)
        {
            var template = await templates.FindAsync(templateId, ct)
                ?? throw new NotFoundException("Whiteboard template not found.");
            if (template.WorkspaceId != workspaceId)
            {
                throw new NotFoundException("Whiteboard template not found.");
            }

            if (template.SeedState is { Length: > 0 } seed)
            {
                await collabState.SeedAsync(wb.Id, workspaceId, seed, ct);
            }
        }

        Audit("whiteboards.whiteboard_created", "Whiteboard", wb.Id, new { wb.Name, wb.IsPrivate, command.LinkedResourceType, command.LinkedResourceId });
        await SaveAsync(ct);
        return ToDto(wb);
    }

    public async Task<WhiteboardDto> UpdateAsync(Guid id, UpdateWhiteboardCommand command, CancellationToken ct)
    {
        var (wb, role) = await LoadForManageAsync(id, ct);
        _ = role;
        wb.UpdateDetails(command.Name, command.IsPrivate, Now);
        Audit("whiteboards.whiteboard_updated", "Whiteboard", wb.Id, new { command.Name, command.IsPrivate });
        await SaveAsync(ct);
        return ToDto(wb);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        var (wb, _) = await LoadForManageAsync(id, ct);
        wb.Archive(Now);
        Audit("whiteboards.whiteboard_archived", "Whiteboard", wb.Id);
        await SaveAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var (wb, _) = await LoadForManageAsync(id, ct);
        whiteboards.Remove(wb);
        Audit("whiteboards.whiteboard_deleted", "Whiteboard", wb.Id);
        await SaveAsync(ct);
    }

    /// <summary>
    /// Uploads an image element dropped onto the canvas. No DB row per image — unlike
    /// Clip/ChatAttachment, an image here has no independent lifecycle of its own; the Yjs shape node that
    /// references it (by <paramref name="imageId"/>, generated here) is the only record of "this image is
    /// still used", exactly like how the shape's x/y/width live only in Yjs state. Requires edit access
    /// (same floor as any other whiteboard content change).
    /// </summary>
    public async Task<(Guid ImageId, string ContentType)> UploadImageAsync(
        Guid id, string? contentType, long sizeBytes, Stream content, CancellationToken ct)
    {
        const long maxImageBytes = 25L * 1024 * 1024;
        if (sizeBytes <= 0)
        {
            throw new ValidationAppException("The uploaded image is empty.");
        }

        if (sizeBytes > maxImageBytes)
        {
            throw new ValidationAppException($"Whiteboard images are limited to {maxImageBytes / (1024 * 1024)} MB.");
        }

        var (wb, _) = await LoadForManageAsync(id, ct);
        var imageId = NewId();
        var safeContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        var validatedContent = await Planvexa.BuildingBlocks.Files.FileContentValidator.ValidateAsync(content, fileName: null, safeContentType, ct);
        await Ctx.MalwareScanner.EnsureCleanAsync(validatedContent, ct);
        await Ctx.FileStorage.SaveAsync(ImagePath(wb.WorkspaceId, wb.Id, imageId), validatedContent, ct);
        Audit("whiteboards.image_uploaded", "Whiteboard", wb.Id, new { imageId, sizeBytes });
        return (imageId, safeContentType);
    }

    public async Task<Stream> DownloadImageAsync(Guid id, Guid imageId, CancellationToken ct)
    {
        var (wb, _) = await LoadForReadAsync(id, ct);
        return await Ctx.FileStorage.OpenReadAsync(ImagePath(wb.WorkspaceId, wb.Id, imageId), ct);
    }

    private static string ImagePath(Guid workspaceId, Guid whiteboardId, Guid imageId)
        => $"workspaces/{workspaceId}/whiteboards/{whiteboardId}/images/{imageId}";

    /// <summary>
    /// The single most important check for Whiteboards: the ONLY thing the Hocuspocus
    /// collaboration server's onAuthenticate hook trusts before admitting a WebSocket connection into a
    /// whiteboard's room. Reachable via GET /api/v1/internal/whiteboards/{id}/can-collaborate, which
    /// requires the same bearer-token authentication as every other endpoint — mirrors
    /// DocumentService.CanCollaborateAsync exactly, extended with the linked-resource ACL check.
    /// </summary>
    public async Task<WhiteboardCollaborationAccessDto> CanCollaborateAsync(Guid id, CancellationToken ct)
    {
        var workspace = Ctx.WorkspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            return new WhiteboardCollaborationAccessDto(false, false, null);
        }

        var role = await RoleAsync(workspace.WorkspaceId, ct);
        if (!WhiteboardsAuthorizer.CanRead(role))
        {
            return new WhiteboardCollaborationAccessDto(false, false, UserId);
        }

        var wb = await whiteboards.FindAsync(id, ct);
        if (wb is null || wb.WorkspaceId != workspace.WorkspaceId || !await CanAccessAsync(wb, role, ct))
        {
            return new WhiteboardCollaborationAccessDto(false, false, UserId);
        }

        var canEdit = WhiteboardsAuthorizer.CanEdit(role)
            && (!wb.IsPrivate || wb.OwnerUserId == UserId || WhiteboardsAuthorizer.CanManage(role));
        return new WhiteboardCollaborationAccessDto(true, canEdit, UserId);
    }

    /// <summary>Effective read access: the structural check on the aggregate (private-to-owner / workspace
    /// floor) ANDed with the linked resource's ACL when linked — see Whiteboard's class doc comment. Used
    /// by every read path (ListAsync, LoadForReadAsync, WhiteboardSearchProvider) so browsing, direct GET,
    /// and search all agree on the exact same rule.</summary>
    internal async Task<bool> CanAccessAsync(Whiteboard wb, WorkspaceRole? role, CancellationToken ct)
    {
        if (!wb.CanBeViewedBy(UserId))
        {
            return false;
        }

        if (wb.LinkedResourceType is null || wb.LinkedResourceId is null)
        {
            return true;
        }

        return await Ctx.LinkedResources.CanViewAsync(wb.WorkspaceId, UserId, wb.LinkedResourceType, wb.LinkedResourceId.Value, ct);
    }

    internal async Task<(Whiteboard Whiteboard, WorkspaceRole? Role)> LoadForReadAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = await RoleAsync(workspaceId, ct);
        WhiteboardsAuthorizer.EnsureRead(role);

        var wb = await whiteboards.FindAsync(id, ct) ?? throw new NotFoundException("Whiteboard not found.");
        if (wb.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Whiteboard not found.");
        }

        if (!await CanAccessAsync(wb, role, ct))
        {
            throw new ForbiddenException("You do not have access to this whiteboard.");
        }

        return (wb, role);
    }

    private async Task<(Whiteboard Whiteboard, WorkspaceRole? Role)> LoadForManageAsync(Guid id, CancellationToken ct)
    {
        var (wb, role) = await LoadForReadAsync(id, ct);
        WhiteboardsAuthorizer.EnsureEdit(role);
        if (wb.IsPrivate && wb.OwnerUserId != UserId && !WhiteboardsAuthorizer.CanManage(role))
        {
            throw new ForbiddenException("Only the whiteboard owner or a workspace administrator can modify this private whiteboard.");
        }

        return (wb, role);
    }

    private static WhiteboardDto ToDto(Whiteboard wb)
        => new(wb.Id, wb.Name, wb.IsPrivate, wb.OwnerUserId, wb.LinkedResourceType, wb.LinkedResourceId, wb.IsArchived, wb.UpdatedAtUtc);
}

/// <summary>Reusable whiteboard content snapshots. See WhiteboardTemplate's doc comment
/// for why capture/apply goes through <see cref="IWhiteboardCollabStateStore"/> rather than a plain
/// string column.</summary>
public sealed class WhiteboardTemplateService(
    WhiteboardServiceContext ctx, IWhiteboardTemplateStore templates, IWhiteboardCollabStateStore collabState, WhiteboardService whiteboardService)
    : WhiteboardServiceBase(ctx)
{
    public async Task<IReadOnlyList<WhiteboardTemplateDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        WhiteboardsAuthorizer.EnsureRead(await RoleAsync(workspaceId, ct));

        var list = await templates.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(t => new WhiteboardTemplateDto(t.Id, t.Name, t.CreatedAtUtc)).ToList();
    }

    public async Task<WhiteboardTemplateDto> CreateFromWhiteboardAsync(Guid whiteboardId, string name, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        WhiteboardsAuthorizer.EnsureEdit(await RoleAsync(workspaceId, ct));

        // Reuses the exact same read-access rule as any other whiteboard read (private/linked included) —
        // a workspace member must not be able to snapshot content of a whiteboard they cannot view.
        var (wb, _) = await whiteboardService.LoadForReadAsync(whiteboardId, ct);

        var seed = await collabState.GetStateAsync(wb.Id, ct);
        var template = WhiteboardTemplate.Create(NewId(), workspaceId, name, seed, UserId, Now);
        templates.Add(template);
        Audit("whiteboards.template_created", "WhiteboardTemplate", template.Id, new { template.Name, sourceWhiteboardId = whiteboardId });
        await SaveAsync(ct);
        return new WhiteboardTemplateDto(template.Id, template.Name, template.CreatedAtUtc);
    }
}
