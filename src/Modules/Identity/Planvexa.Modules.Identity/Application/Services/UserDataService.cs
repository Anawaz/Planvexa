namespace Planvexa.Modules.Identity.Application.Services;

using System.IO.Compression;
using System.Text.Json;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Identity.Application;
using Planvexa.Modules.Identity.Domain;
using Planvexa.SharedContracts.UserData;
using Planvexa.SharedContracts.Users;

/// <summary>Row shape written into export.zip's profile.json.</summary>
public sealed record UserProfileExport(
    Guid Id, string Email, string DisplayName, string Subject, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastSeenAtUtc);

/// <summary>What <see cref="UserDataService.DeleteAsync"/> did, for the API response and for the caller's own records.</summary>
public sealed record UserDeletionSummary(Guid UserId, int PersonalAccessTokensDeleted, DateTimeOffset AnonymizedAtUtc);

/// <summary>
/// GDPR-style export/deletion of a single user's OWN data. Self-service only, by
/// design (see the report for this task): every method acts on <see cref="ICurrentUser.UserId"/>, never on
/// an id supplied by the caller, so there is no authorization check to get wrong — a user structurally
/// cannot reach another user's data through this service. A Workspace Owner acting on a member's behalf
/// was considered and rejected: a global identity can belong to Workspaces the acting Owner has no
/// visibility into, so an Owner-triggered export/delete would either leak data from those other
/// Workspaces into the Owner's hands or silently scope-narrow in a way that undermines "delete my
/// account" — both worse than requiring the member to act on their own account (or an Owner to ask a
/// platform admin, outside this task's scope).
/// </summary>
public sealed class UserDataService(
    IUserStore users,
    IUserDataQuery query,
    IUserDataEraser eraser,
    ICurrentUser currentUser,
    IClock clock,
    IAuditWriter audit,
    IUnitOfWork unitOfWork)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Builds a zip archive (one JSON file per data category — the same zip-of-files export shape as
    /// Governance's CSV exports, just JSON here since the shapes are nested/heterogeneous rather than
    /// flat rows) containing everything reasonably attributable to the caller as a person: their own
    /// profile, the Workspaces they belong to, tasks they created or are assigned to, comments they
    /// authored, and time entries they logged. Deliberately excludes rows that merely reference the
    /// user's id incidentally (e.g. every task in a shared Workspace) — this is a personal export, not a
    /// Workspace export.
    /// </summary>
    public async Task<byte[]> ExportAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var user = await users.FindByIdAsync(userId, ct)
            ?? throw new NotFoundException("User not found.");

        var memberships = await query.GetMembershipsAsync(userId, ct);
        var tasks = await query.GetTasksAsync(userId, ct);
        var comments = await query.GetCommentsAsync(userId, ct);
        var timeEntries = await query.GetTimeEntriesAsync(userId, ct);

        var profile = new UserProfileExport(user.Id, user.Email, user.DisplayName, user.Subject, user.CreatedAtUtc, user.LastSeenAtUtc);

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteJsonEntryAsync(zip, "profile.json", profile, ct);
            await WriteJsonEntryAsync(zip, "workspace-memberships.json", memberships, ct);
            await WriteJsonEntryAsync(zip, "tasks.json", tasks, ct);
            await WriteJsonEntryAsync(zip, "comments.json", comments, ct);
            await WriteJsonEntryAsync(zip, "time-entries.json", timeEntries, ct);
        }

        audit.Write("identity.user.exported", nameof(User), userId, new
        {
            workspaces = memberships.Count,
            tasks = tasks.Count,
            comments = comments.Count,
            timeEntries = timeEntries.Count,
        });
        await unitOfWork.SaveChangesAsync(ct);

        return stream.ToArray();
    }

    /// <summary>
    /// Deletes the caller's own account: hard-deletes what is safe to hard-delete (personal access
    /// tokens — pure credentials nothing else references), and anonymizes the User row in place for
    /// everything else (see <see cref="User.Anonymize"/>'s doc comment — tasks/comments/time entries in
    /// other modules are NOT touched; they keep referencing this same UserId, which now resolves to the
    /// scrubbed "Deleted User" values, so thread/audit structure survives intact). Refuses to run while
    /// the caller is the sole active Owner of a Workspace, mirroring the existing
    /// MembershipService.LeaveAsync guard ("a workspace must always have an Owner") — the caller must
    /// transfer ownership first.
    /// </summary>
    public async Task<UserDeletionSummary> DeleteAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var user = await users.FindByIdAsync(userId, ct)
            ?? throw new NotFoundException("User not found.");

        var memberships = await query.GetMembershipsAsync(userId, ct);
        var soleOwnedWorkspaces = memberships.Where(m => m.IsSoleActiveOwner).Select(m => m.WorkspaceName).ToList();
        if (soleOwnedWorkspaces.Count > 0)
        {
            throw new ConflictException(
                $"Transfer ownership before deleting your account — you are the sole Owner of: {string.Join(", ", soleOwnedWorkspaces)}.");
        }

        var patsDeleted = await eraser.DeletePersonalAccessTokensAsync(userId, ct);

        var now = clock.UtcNow;
        user.Anonymize(now);

        audit.Write("identity.user.deleted", nameof(User), userId, new { personalAccessTokensDeleted = patsDeleted });
        await unitOfWork.SaveChangesAsync(ct);

        return new UserDeletionSummary(userId, patsDeleted, now);
    }

    /// <summary>Self-service profile edit: renames the caller's own account and sets their display
    /// preferences. See <see cref="User.UpdateDisplayName"/> and <see cref="User.SetPreferences"/>.</summary>
    public async Task<UserInfo> UpdateDisplayNameAsync(string displayName, string? timezone, string? locale, string? theme, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var user = await users.FindByIdAsync(userId, ct)
            ?? throw new NotFoundException("User not found.");

        var now = clock.UtcNow;
        user.UpdateDisplayName(displayName, now);
        user.SetPreferences(timezone, locale, theme, now);

        audit.Write(
            "identity.user.profile_updated",
            nameof(User),
            userId,
            new { displayName = user.DisplayName, timezone = user.Timezone, locale = user.Locale, theme = user.Theme });
        await unitOfWork.SaveChangesAsync(ct);

        return new UserInfo(user.Id, user.Email, user.DisplayName, user.AvatarUrl, user.Timezone, user.Locale, user.Theme);
    }

    private static async Task WriteJsonEntryAsync<T>(ZipArchive zip, string entryName, T value, CancellationToken ct)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
        await using var entryStream = entry.Open();
        await JsonSerializer.SerializeAsync(entryStream, value, JsonOptions, ct);
    }
}
