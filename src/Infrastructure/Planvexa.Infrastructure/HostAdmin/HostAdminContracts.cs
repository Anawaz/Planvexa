namespace Planvexa.Infrastructure.HostAdmin;

/// <summary>
/// Read models for the host administration console. Metadata and aggregates ONLY — no task titles,
/// document bodies, chat messages or any other Workspace content appears in this file, and nothing
/// here should ever be widened to include it. Workspace remains the isolation boundary for content;
/// host administration is about the installation, not what people are working on inside it.
/// </summary>
public sealed record HostWorkspaceSummary(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    DateTimeOffset CreatedAtUtc,
    Guid? OwnerUserId,
    string? OwnerDisplayName,
    string? OwnerEmail,
    int MemberCount,
    DateTimeOffset? LastActivityAtUtc);

public sealed record HostWorkspaceMember(
    Guid MembershipId,
    Guid UserId,
    string? DisplayName,
    string? Email,
    string Role,
    string Status,
    bool IsGuest,
    DateTimeOffset JoinedAtUtc);

public sealed record HostWorkspaceDetail(
    HostWorkspaceSummary Summary,
    IReadOnlyList<string> EnabledFeatures,
    IReadOnlyList<HostWorkspaceMember> Members);

/// <summary>Per-workspace content volume — counts and bytes, never the content itself.</summary>
public sealed record HostWorkspaceUsage(
    Guid WorkspaceId,
    int Spaces,
    int Lists,
    int Tasks,
    int Documents,
    int Attachments,
    long AttachmentBytes);

public sealed record HostUserSummary(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsActive,
    bool IsHostAdmin,
    bool IsAnonymized,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    int WorkspaceCount);

public sealed record HostUserMembership(
    Guid WorkspaceId,
    string WorkspaceName,
    string WorkspaceSlug,
    string WorkspaceStatus,
    string Role,
    string Status,
    DateTimeOffset JoinedAtUtc);

public sealed record HostUserDetail(HostUserSummary Summary, IReadOnlyList<HostUserMembership> Memberships);

/// <summary>One month of the workspace-creation trend on the overview page.</summary>
public sealed record HostMonthlyCount(int Year, int Month, int Count);

public sealed record HostActivityEntry(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    string Action,
    string EntityType,
    Guid? EntityId,
    Guid? ActorUserId,
    string? ActorDisplayName,
    Guid? WorkspaceId,
    string? WorkspaceName,
    string? IpAddress);

public sealed record HostOverview(
    int ActiveWorkspaces,
    int ArchivedWorkspaces,
    int ActiveUsers,
    int DisabledUsers,
    int HostAdmins,
    int Memberships,
    int UsersSeenLast7Days,
    int UsersSeenLast30Days,
    IReadOnlyList<HostMonthlyCount> WorkspacesCreatedByMonth,
    IReadOnlyList<HostActivityEntry> RecentActivity);

/// <summary>A page of results plus the unfiltered-by-paging total, so the UI can show "x of y".</summary>
public sealed record HostPage<T>(IReadOnlyList<T> Items, int Total);
