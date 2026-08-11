namespace Planvexa.Modules.Collaboration.Application;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Collaboration.Domain;
using Planvexa.SharedContracts.Governance;
using Planvexa.SharedContracts.Work;
using Planvexa.SharedContracts.Workspaces;

public sealed class ShareLinkService(
    IWorkspaceContextAccessor workspaceAccessor,
    ICurrentUser currentUser,
    IShareLinkStore links,
    IPublicCommentStore publicComments,
    ITaskDirectory tasks,
    IWorkspaceAccessQuery access,
    IAuditQuery auditQuery,
    IAuditWriter audit,
    IIdGenerator ids,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<ShareLinkDto> CreateAsync(
        Guid taskId, int? expiresInDays, string? password = null, PermissionLevel? permissionLevel = null, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Task not found.");
        var callerAccess = await access.GetAccessAsync(task.WorkspaceId, currentUser.UserId, ct);
        if (callerAccess is null || callerAccess.Role < WorkspaceRole.Member)
        {
            throw new ForbiddenException("You do not have permission to share tasks in this workspace.");
        }

        var validFor = expiresInDays is > 0 ? TimeSpan.FromDays(expiresInDays.Value) : (TimeSpan?)null;
        var level = permissionLevel ?? PermissionLevel.View;
        var (link, rawToken) = PublicShareLink.Create(
            ids.NewId(), task.WorkspaceId, task.TaskId, currentUser.UserId, clock.UtcNow, validFor, level);
        if (!string.IsNullOrEmpty(password))
        {
            link.SetPassword(password);
        }

        links.Add(link);
        audit.Write("task.shared", nameof(PublicShareLink), link.Id,
            new { task.TaskId, passwordProtected = link.RequiresPassword, permissionLevel = link.Level.ToString() });
        await unitOfWork.SaveChangesAsync(ct);

        return new ShareLinkDto(link.Id, link.TaskId, rawToken, $"/public/tasks/{rawToken}", link.ExpiresAtUtc, link.RequiresPassword, link.Level);
    }

    public async Task<IReadOnlyList<ShareLinkDto>> ListForTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Task not found.");
        var callerAccess = await access.GetAccessAsync(task.WorkspaceId, currentUser.UserId, ct);
        if (callerAccess is null)
        {
            throw new ForbiddenException("You do not have access to this workspace.");
        }

        var list = await links.ListForTaskAsync(taskId, ct);
        return list.Where(l => !l.IsRevoked)
            .Select(l => new ShareLinkDto(l.Id, l.TaskId, string.Empty, $"/public/tasks/…", l.ExpiresAtUtc, l.RequiresPassword, l.Level))
            .ToList();
    }

    public async Task RevokeAsync(Guid shareId, CancellationToken ct = default)
    {
        var link = await links.FindAsync(shareId, ct) ?? throw new NotFoundException("Share link not found.");
        var callerAccess = await access.GetAccessAsync(link.WorkspaceId, currentUser.UserId, ct);
        if (callerAccess is null || callerAccess.Role < WorkspaceRole.Member)
        {
            throw new ForbiddenException("You do not have permission to revoke this share.");
        }

        link.Revoke();
        audit.Write("task.share_revoked", nameof(PublicShareLink), link.Id);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Anonymous read path. Resolves the link by raw token, verifies the password when the link
    /// requires one, establishes the link's workspace context, then returns ONLY the shared task's
    /// projection — never siblings, comments, or other workspace data. Every attempt (found or not,
    /// password right or wrong) is audited via <see cref="IAuditWriter"/> with the caller's IP so the
    /// link owner can review access through <see cref="ListAccessLogAsync"/>.
    /// </summary>
    public async Task<SharedTaskAccessResult> GetSharedTaskAsync(string rawToken, string? password, string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return SharedTaskAccessResult.NotFound;
        }

        var link = await links.FindByTokenHashAsync(PublicShareLink.HashToken(rawToken), ct);
        if (link is null || !link.IsUsable(clock.UtcNow))
        {
            // No resolvable workspace for a bad/expired/revoked token: audited as a platform-level event
            // (IAuditWriter.Write reads the ambient workspace, which stays unset here).
            audit.Write("share_link.access_denied", nameof(PublicShareLink), link?.Id,
                new { reason = link is null ? "not_found" : "expired_or_revoked" }, ipAddress);
            await unitOfWork.SaveChangesAsync(ct);
            return SharedTaskAccessResult.NotFound;
        }

        // Establish the link's workspace context before the password check (not just after, like the
        // earlier version did) so a DENIED attempt is scoped to the right workspace in the audit log
        // too, not only a successful one.
        SetLinkWorkspaceContext(link);

        if (link.RequiresPassword)
        {
            if (string.IsNullOrEmpty(password))
            {
                audit.Write("share_link.access_denied", nameof(PublicShareLink), link.Id, new { reason = "password_required" }, ipAddress);
                await unitOfWork.SaveChangesAsync(ct);
                return SharedTaskAccessResult.PasswordRequired;
            }

            if (!link.VerifyPassword(password))
            {
                audit.Write("share_link.access_denied", nameof(PublicShareLink), link.Id, new { reason = "invalid_password" }, ipAddress);
                await unitOfWork.SaveChangesAsync(ct);
                return SharedTaskAccessResult.InvalidPassword;
            }
        }

        var task = await tasks.FindAsync(link.TaskId, ct);
        if (task is null)
        {
            audit.Write("share_link.access_denied", nameof(PublicShareLink), link.Id, new { reason = "task_not_found" }, ipAddress);
            await unitOfWork.SaveChangesAsync(ct);
            return SharedTaskAccessResult.NotFound;
        }

        audit.Write("share_link.accessed", nameof(PublicShareLink), link.Id, new { permissionLevel = link.Level.ToString() }, ipAddress);
        await unitOfWork.SaveChangesAsync(ct);

        return new SharedTaskAccessResult(
            ShareLinkAccessStatus.Ok, new SharedTaskDto(task.TaskId, task.Title, Description: null, task.IsCompleted, link.AllowsComments));
    }

    /// <summary>
    /// Anonymous comment path, gated on the link granting Comment (not View-only) permission — see
    /// <see cref="PublicShareLink.AllowsComments"/>. Never touches the internal <see cref="Comment"/>
    /// aggregate (that requires a real workspace-member author); a guest comment is a separate,
    /// display-only record the link owner reads via <see cref="ListPublicCommentsAsync"/>. There is no
    /// anonymous edit/delete endpoint for either kind of comment, so "view + comment, never edit" holds
    /// by construction, not by a runtime check.
    /// </summary>
    public async Task<PublicCommentPostResult> AddPublicCommentAsync(
        string rawToken, string? password, string? guestName, string body, string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return PublicCommentPostResult.NotFound;
        }

        var link = await links.FindByTokenHashAsync(PublicShareLink.HashToken(rawToken), ct);
        if (link is null || !link.IsUsable(clock.UtcNow))
        {
            audit.Write("share_link.comment_denied", nameof(PublicShareLink), link?.Id,
                new { reason = link is null ? "not_found" : "expired_or_revoked" }, ipAddress);
            await unitOfWork.SaveChangesAsync(ct);
            return PublicCommentPostResult.NotFound;
        }

        SetLinkWorkspaceContext(link);

        if (link.RequiresPassword)
        {
            if (string.IsNullOrEmpty(password))
            {
                audit.Write("share_link.comment_denied", nameof(PublicShareLink), link.Id, new { reason = "password_required" }, ipAddress);
                await unitOfWork.SaveChangesAsync(ct);
                return PublicCommentPostResult.PasswordRequired;
            }

            if (!link.VerifyPassword(password))
            {
                audit.Write("share_link.comment_denied", nameof(PublicShareLink), link.Id, new { reason = "invalid_password" }, ipAddress);
                await unitOfWork.SaveChangesAsync(ct);
                return PublicCommentPostResult.InvalidPassword;
            }
        }

        if (!link.AllowsComments)
        {
            audit.Write("share_link.comment_denied", nameof(PublicShareLink), link.Id, new { reason = "view_only" }, ipAddress);
            await unitOfWork.SaveChangesAsync(ct);
            return PublicCommentPostResult.Forbidden;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return PublicCommentPostResult.Invalid;
        }

        var comment = PublicComment.Create(ids.NewId(), link.WorkspaceId, link.Id, link.TaskId, guestName, body, clock.UtcNow, ipAddress);
        publicComments.Add(comment);
        audit.Write("share_link.comment_posted", nameof(PublicShareLink), link.Id, new { commentId = comment.Id }, ipAddress);
        await unitOfWork.SaveChangesAsync(ct);

        return PublicCommentPostResult.Ok(new PublicCommentDto(comment.Id, comment.GuestName, comment.Body, comment.CreatedAtUtc, null));
    }

    /// <summary>Guest comments left on a link, for the link's workspace to review (never surfaced to other anonymous visitors).</summary>
    public async Task<IReadOnlyList<PublicCommentDto>> ListPublicCommentsAsync(Guid shareId, CancellationToken ct = default)
    {
        var link = await links.FindAsync(shareId, ct) ?? throw new NotFoundException("Share link not found.");
        var callerAccess = await access.GetAccessAsync(link.WorkspaceId, currentUser.UserId, ct);
        if (callerAccess is null || callerAccess.Role < WorkspaceRole.Member)
        {
            throw new ForbiddenException("You do not have permission to view this share's comments.");
        }

        var list = await publicComments.ListForShareLinkAsync(shareId, ct);
        return list.OrderBy(c => c.CreatedAtUtc)
            .Select(c => new PublicCommentDto(c.Id, c.GuestName, c.Body, c.CreatedAtUtc, c.IpAddress))
            .ToList();
    }

    /// <summary>
    /// Every access attempt recorded against this link (success and denial), for the link owner. Reuses
    /// the Governance module's existing <see cref="IAuditQuery"/> cross-module read contract instead of a
    /// new query mechanism — see note on reusing the Audit module's pattern.
    /// </summary>
    public async Task<IReadOnlyList<ShareAccessLogEntryDto>> ListAccessLogAsync(Guid shareId, CancellationToken ct = default)
    {
        var link = await links.FindAsync(shareId, ct) ?? throw new NotFoundException("Share link not found.");
        var callerAccess = await access.GetAccessAsync(link.WorkspaceId, currentUser.UserId, ct);
        if (callerAccess is null || callerAccess.Role < WorkspaceRole.Member)
        {
            throw new ForbiddenException("You do not have permission to view this share's access log.");
        }

        var records = await auditQuery.SearchAsync(
            link.WorkspaceId,
            new AuditFilter(Action: null, EntityType: nameof(PublicShareLink), ActorUserId: null, FromUtc: null, ToUtc: null, EntityId: shareId),
            ct);
        return records.Select(r => new ShareAccessLogEntryDto(r.Id, r.Action, r.CreatedAtUtc, r.IpAddress)).ToList();
    }

    private void SetLinkWorkspaceContext(PublicShareLink link)
        => workspaceAccessor.Set(new WorkspaceContext(
            workspaceId: link.WorkspaceId,
            userId: Guid.Empty,
            membershipId: null,
            role: string.Empty,
            permissions: new HashSet<string>(),
            entitlements: new HashSet<string>(),
            correlationId: workspaceAccessor.Current.CorrelationId));
}
