namespace Planvexa.Modules.Identity.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Files;
using Planvexa.Modules.Identity.Domain;
using Planvexa.SharedContracts.Users;

/// <summary>
/// Profile picture upload for the caller's own account. Same magic-byte-validate + malware-scan +
/// <see cref="IFileStorage"/> pipeline as every other upload path in this codebase (see
/// WorkManagement's AttachmentService, DocumentService.UploadImageAsync) — no signed URLs, served by
/// straight bearer-authenticated streaming instead (see AvatarEndpoints), same as document/whiteboard
/// inline images. One deterministic path per user (no attachment id): a re-upload just overwrites the
/// previous blob, so there is no history/orphan-cleanup bookkeeping to do.
/// </summary>
public sealed class AvatarService(
    IUserStore users, IFileStorage storage, IMalwareScanner scanner, ICurrentUser currentUser,
    IClock clock, IAuditWriter audit, IUnitOfWork unitOfWork)
{
    public const long MaxAvatarBytes = 5L * 1024 * 1024;

    public async Task<UserInfo> UploadAsync(string? contentType, long sizeBytes, Stream content, CancellationToken ct)
    {
        if (sizeBytes <= 0)
        {
            throw new ValidationAppException("The uploaded file is empty.");
        }

        if (sizeBytes > MaxAvatarBytes)
        {
            throw new ValidationAppException($"Avatars are limited to {MaxAvatarBytes / (1024 * 1024)} MB.");
        }

        if (string.IsNullOrWhiteSpace(contentType) || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationAppException("Avatars must be an image file.");
        }

        var userId = currentUser.UserId;
        var user = await users.FindByIdAsync(userId, ct) ?? throw new NotFoundException("User not found.");

        var validatedContent = await FileContentValidator.ValidateAsync(content, fileName: null, contentType, ct);
        await scanner.EnsureCleanAsync(validatedContent, ct);
        await storage.SaveAsync(AvatarPath(userId), validatedContent, ct);

        user.SetAvatarUrl($"/users/{userId}/avatar", clock.UtcNow);

        audit.Write("identity.user.avatar_updated", nameof(User), userId, new { sizeBytes });
        await unitOfWork.SaveChangesAsync(ct);

        return new UserInfo(user.Id, user.Email, user.DisplayName, user.AvatarUrl);
    }

    /// <summary>
    /// Streams a user's avatar by id. Deliberately not workspace-scoped or restricted to shared-workspace
    /// members: an avatar picture is no more sensitive than the DisplayName any authenticated user can
    /// already resolve for an arbitrary UserId via other modules' actor fields, and gating it on shared
    /// membership would require this global-identity module to reach into Tenancy's membership table
    /// (forbidden — AGENTS.md rule 7).
    /// </summary>
    public async Task<Stream> DownloadAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId, ct);
        if (user?.AvatarUrl is null)
        {
            throw new NotFoundException("User has no avatar.");
        }

        return await storage.OpenReadAsync(AvatarPath(userId), ct);
    }

    private static string AvatarPath(Guid userId) => $"users/{userId}/avatar";
}
