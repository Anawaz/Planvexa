namespace Planvexa.Api.Auth;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.SharedContracts.Users;

/// <summary>
/// Authorization for the host administration console — instance-level administration of this
/// Planvexa installation, distinct from Workspace administration (<c>TenancyAuthorizer</c>,
/// <c>GovernanceAuthorizer</c>), which is always scoped to one Workspace and reached through a
/// Workspace role. A host administrator is typically a member of no Workspace at all.
///
/// Deliberately NOT a Workspace permission key: <c>/host/*</c> endpoints run with no
/// <c>X-Workspace</c> header, so there is no ambient Workspace, no membership and no resolved
/// permission set to check against.
/// </summary>
public sealed class HostAdminRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "HostAdmin";
}

/// <summary>
/// Grants the <see cref="HostAdminRequirement.PolicyName"/> policy when the authenticated caller is
/// flagged <c>identity.users.is_host_admin</c> (and still active), or — as a break-glass path — when
/// their identity-provider subject is listed in <c>HostAdmin:Subjects</c>.
///
/// The break-glass list exists because the flag is self-administered: the last host admin could
/// otherwise be disabled or demoted (the console's own guards make that hard, but a direct database
/// edit or a lost account does not go through them) leaving an installation with no way back in.
/// Empty by default; changing it requires filesystem/env access to the server, which is the same
/// trust level as the database itself.
///
/// Runs per request, so revoking the flag takes effect on the caller's very next call — no token
/// refresh, no session invalidation. <see cref="ICurrentUser"/> is populated by
/// <see cref="Middleware.UserContextMiddleware"/>, which runs earlier in the pipeline than
/// <c>UseAuthorization</c> (see Program.cs); that same middleware is where a deactivated account is
/// already rejected, so this handler never sees one.
/// </summary>
public sealed class HostAdminAuthorizationHandler(
    ICurrentUser currentUser,
    IUserDirectory users,
    IConfiguration configuration) : AuthorizationHandler<HostAdminRequirement>
{
    public const string BreakGlassSubjectsKey = "HostAdmin:Subjects";

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, HostAdminRequirement requirement)
    {
        if (!currentUser.IsAuthenticated)
        {
            return;
        }

        if (IsBreakGlassSubject(configuration, currentUser.Subject)
            || await users.IsHostAdminAsync(currentUser.UserId))
        {
            context.Succeed(requirement);
        }
    }

    /// <summary>
    /// Reads <c>HostAdmin:Subjects</c> in both shapes an operator might supply it: a JSON array in
    /// appsettings, or a single comma-separated value (which is what
    /// <c>HostAdmin__Subjects=a,b</c> in a container environment produces — the indexed
    /// <c>HostAdmin__Subjects__0</c> form binds as an array and is covered by the first case).
    /// </summary>
    public static bool IsBreakGlassSubject(IConfiguration configuration, string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return false;
        }

        var section = configuration.GetSection(BreakGlassSubjectsKey);
        var configured = section.Get<string[]>()
            ?? section.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];

        return configured.Any(candidate => string.Equals(candidate.Trim(), subject, StringComparison.Ordinal));
    }
}
