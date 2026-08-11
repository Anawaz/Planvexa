namespace Planvexa.Modules.Tenancy.Application;

using Planvexa.Modules.Tenancy.Authorization;
using Planvexa.Modules.Tenancy.Domain;

/// <summary>
/// The five built-in roles seeded into every workspace (ADR-0003): Owner, Admin, Member,
/// Limited Member, and Guest. This is the single source of truth for both the in-process seeding path
/// (<see cref="WorkspaceRegistrationService"/>, new workspaces) and the DbUp backfill script
/// (0032_SeedBuiltInRolesForExistingWorkspaces.sql, existing workspaces) — keep the SQL script's VALUES
/// in sync if this catalog changes; DbUp scripts are immutable once shipped, so a later drift would
/// need a follow-up backfill script rather than an edit to 0032.
///
/// Judgment calls (no prior product spec to follow exactly):
/// - Admin gets everything Owner does except roles.manage (matches the original hardcoded model).
/// - Member gets space view/edit and task view/comment/edit, not the *.manage/task.share keys (those
///   stay Admin+ until the per-resource ACL lets an individual Member own/manage a specific item).
/// - Limited Member (new) is a strict subset of Member: task view/comment/edit + features.view only —
///   no members.view (can't browse the member directory) and no space.view/edit (can't browse workspace
///   structure). The intent: scoped to the items they're given, not the
///   workspace at large. Actual "only items assigned/shared to them" scoping is the per-resource ACL;
///   this change only narrows the coarse permission vocabulary.
/// - Guest gets task.view only ("view only unless explicitly granted" — explicit grants come from
///   share links / the per-task ACL, not from a built-in role permission).
/// </summary>
public static class BuiltInRoles
{
    public sealed record Definition(MembershipRole Role, string Key, string Name, IReadOnlySet<string> Permissions);

    public static readonly IReadOnlyList<Definition> All =
    [
        new(MembershipRole.Owner, "owner", "Owner", new HashSet<string>
        {
            TenancyPermissions.WorkspaceManage, TenancyPermissions.MembersView, TenancyPermissions.MembersInvite,
            TenancyPermissions.MembersManage, TenancyPermissions.RolesManage, TenancyPermissions.FeaturesView,
            TenancyPermissions.SpaceView, TenancyPermissions.SpaceEdit, TenancyPermissions.SpaceManage,
            TenancyPermissions.TaskView, TenancyPermissions.TaskComment, TenancyPermissions.TaskEdit,
            TenancyPermissions.TaskManage, TenancyPermissions.TaskShare,
        }),
        new(MembershipRole.Admin, "admin", "Admin", new HashSet<string>
        {
            TenancyPermissions.WorkspaceManage, TenancyPermissions.MembersView, TenancyPermissions.MembersInvite,
            TenancyPermissions.MembersManage, TenancyPermissions.FeaturesView,
            TenancyPermissions.SpaceView, TenancyPermissions.SpaceEdit, TenancyPermissions.SpaceManage,
            TenancyPermissions.TaskView, TenancyPermissions.TaskComment, TenancyPermissions.TaskEdit,
            TenancyPermissions.TaskManage, TenancyPermissions.TaskShare,
        }),
        new(MembershipRole.Member, "member", "Member", new HashSet<string>
        {
            TenancyPermissions.MembersView, TenancyPermissions.FeaturesView,
            TenancyPermissions.SpaceView, TenancyPermissions.SpaceEdit,
            TenancyPermissions.TaskView, TenancyPermissions.TaskComment, TenancyPermissions.TaskEdit,
        }),
        new(MembershipRole.LimitedMember, "limited_member", "Limited Member", new HashSet<string>
        {
            TenancyPermissions.FeaturesView,
            TenancyPermissions.TaskView, TenancyPermissions.TaskComment, TenancyPermissions.TaskEdit,
        }),
        new(MembershipRole.Guest, "guest", "Guest", new HashSet<string>
        {
            TenancyPermissions.TaskView,
        }),
    ];

    public static Definition For(MembershipRole role) =>
        All.FirstOrDefault(d => d.Role == role) ?? All.Single(d => d.Role == MembershipRole.Guest);

    public static string KeyFor(MembershipRole role) => For(role).Key;
}
