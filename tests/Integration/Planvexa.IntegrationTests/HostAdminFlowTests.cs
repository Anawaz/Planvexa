namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Npgsql;
using Shouldly;
using Xunit;

/// <summary>
/// Host administration — the instance-level console. The fixture runs the API as a NOBYPASSRLS,
/// non-owner role against a real PostgreSQL container, so these tests prove the host-admin RLS
/// policies from script 0094 and not merely the C#-side authorization: without those policies the
/// cross-workspace reads below return zero rows and the assertions fail.
/// </summary>
[Collection("api")]
public sealed class HostAdminFlowTests(PlanvexaFixture fixture)
{
    private sealed record HostAdminProbe(bool IsHostAdmin);
    private sealed record HostPage<T>(List<T> Items, int Total);
    private sealed record HostWorkspaceSummary(
        Guid Id, string Name, string Slug, string Status, DateTimeOffset CreatedAtUtc,
        Guid? OwnerUserId, string? OwnerDisplayName, string? OwnerEmail, int MemberCount,
        DateTimeOffset? LastActivityAtUtc);
    private sealed record HostWorkspaceMember(
        Guid MembershipId, Guid UserId, string? DisplayName, string? Email,
        string Role, string Status, bool IsGuest, DateTimeOffset JoinedAtUtc);
    private sealed record HostWorkspaceDetail(
        HostWorkspaceSummary Summary, List<string> EnabledFeatures, List<HostWorkspaceMember> Members);
    private sealed record HostWorkspaceUsage(
        Guid WorkspaceId, int Spaces, int Lists, int Tasks, int Documents, int Attachments, long AttachmentBytes);
    private sealed record HostUserSummary(
        Guid Id, string Email, string DisplayName, bool IsActive, bool IsHostAdmin, bool IsAnonymized,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? LastSeenAtUtc, int WorkspaceCount);
    private sealed record HostUserMembership(
        Guid WorkspaceId, string WorkspaceName, string WorkspaceSlug, string WorkspaceStatus,
        string Role, string Status, DateTimeOffset JoinedAtUtc);
    private sealed record HostUserDetail(HostUserSummary Summary, List<HostUserMembership> Memberships);
    private sealed record HostActivityEntry(
        Guid Id, DateTimeOffset CreatedAtUtc, string Action, string EntityType, Guid? EntityId,
        Guid? ActorUserId, string? ActorDisplayName, Guid? WorkspaceId, string? WorkspaceName, string? IpAddress);
    private sealed record InstanceHealthResponse(
        bool DatabaseReachable, string? DatabaseVersion, int AppliedScripts, string? LatestScript,
        int OutboxPending, int OutboxFailed, int ErrorsLast24Hours, int WarningsLast24Hours,
        int DroppedLogRecords, bool LogCaptureEnabled, string LogMinimumLevel, int LogRetentionDays,
        string FileStorageProvider, string EmailSender, bool MaintenanceConnectionConfigured,
        string? Version, string Environment);
    private sealed record InstanceLogResponse(
        Guid Id, DateTimeOffset CreatedAtUtc, string Level, string Category, string Message,
        string? Exception, string? CorrelationId, Guid? UserId, Guid? WorkspaceId);
    private sealed record InstanceSettingsResponse(
        bool AllowSelfRegistration, string WorkspaceCreationPolicy, string? InstanceName, string? LogoUrl,
        string? SupportEmail, DateTimeOffset? UpdatedAtUtc, Guid? UpdatedByUserId);
    private sealed record PublicPolicyResponse(
        bool AllowSelfRegistration, string WorkspaceCreationPolicy, string? InstanceName, string? LogoUrl,
        string? SupportEmail);
    private sealed record HostOverview(
        int ActiveWorkspaces, int ArchivedWorkspaces, int ActiveUsers, int DisabledUsers, int HostAdmins,
        int Memberships, int UsersSeenLast7Days, int UsersSeenLast30Days,
        List<object> WorkspacesCreatedByMonth, List<HostActivityEntry> RecentActivity);

    /// <summary>
    /// Flips identity.users.is_host_admin directly, over the fixture's superuser connection — the same
    /// state a real installation reaches through PlanvexaBootstrap or an existing host admin's grant.
    /// Deliberately NOT the HostAdmin:Subjects config break-glass: that path bypasses the database flag
    /// the RLS policies read, so using it here would test nothing about isolation.
    /// </summary>
    private async Task<Guid> MakeHostAdminAsync(string subject)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE identity.users SET is_host_admin = true WHERE subject = @subject RETURNING id;";
        command.Parameters.AddWithValue("subject", subject);
        var id = await command.ExecuteScalarAsync();
        id.ShouldNotBeNull($"No user row exists for subject '{subject}' — sign in once before promoting.");
        return (Guid)id!;
    }

    private static async Task SetHostAdminFlagAsync(PlanvexaFixture fixture, string subject, bool value)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE identity.users SET is_host_admin = @value WHERE subject = @subject;";
        command.Parameters.AddWithValue("value", value);
        command.Parameters.AddWithValue("subject", subject);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// A host administrator with their own workspace (so the account exists) plus a SEPARATE workspace
    /// owned by somebody else that the host admin is deliberately not a member of — the whole point of
    /// the console is seeing that second one.
    /// </summary>
    private async Task<(HttpClient Host, string HostSubject, Guid HostUserId,
        HttpClient Stranger, string StrangerSubject, WorkspaceResponse StrangerWorkspace)> SetupAsync(string prefix)
    {
        var hostSubject = TestData.NewSubject();
        var (hostRegister, _) = await fixture.AuthClient(hostSubject).RegisterOrgAsync(TestData.NewSlug($"{prefix}h"));
        hostRegister.EnsureSuccessStatusCode();
        var hostUserId = await MakeHostAdminAsync(hostSubject);

        var strangerSubject = TestData.NewSubject();
        var (strangerRegister, strangerWorkspace) =
            await fixture.AuthClient(strangerSubject).RegisterOrgAsync(TestData.NewSlug($"{prefix}s"));
        strangerRegister.EnsureSuccessStatusCode();

        // No X-Workspace on the host client: /host/* is instance-level and its cross-workspace reads
        // depend on there being no ambient workspace at all.
        return (fixture.AuthClient(hostSubject), hostSubject, hostUserId,
            fixture.AuthClient(strangerSubject, strangerWorkspace.Id), strangerSubject, strangerWorkspace);
    }

    // ---- access control ----

    [Fact]
    public async Task Every_host_route_is_forbidden_for_a_non_host_admin_even_a_workspace_owner()
    {
        var ownerSubject = TestData.NewSubject();
        var (register, workspace) = await fixture.AuthClient(ownerSubject).RegisterOrgAsync(TestData.NewSlug("hdeny"));
        register.EnsureSuccessStatusCode();

        // Owner of their own workspace — the highest Workspace role there is — and still not a host admin.
        var owner = fixture.AuthClient(ownerSubject, workspace.Id);

        foreach (var route in new[]
                 {
                     "/api/v1/host/overview",
                     "/api/v1/host/workspaces",
                     $"/api/v1/host/workspaces/{workspace.Id}",
                     $"/api/v1/host/workspaces/{workspace.Id}/usage",
                     "/api/v1/host/users",
                     $"/api/v1/host/users/{Guid.NewGuid()}",
                     "/api/v1/host/activity",
                 })
        {
            (await owner.GetAsync(new Uri(route, UriKind.Relative)))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"GET {route} must be host-admin only.");
        }

        foreach (var route in new[]
                 {
                     $"/api/v1/host/workspaces/{workspace.Id}/suspend",
                     $"/api/v1/host/workspaces/{workspace.Id}/restore",
                     $"/api/v1/host/users/{Guid.NewGuid()}/disable",
                     $"/api/v1/host/users/{Guid.NewGuid()}/enable",
                 })
        {
            (await owner.PostAsync(new Uri(route, UriKind.Relative), null))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"POST {route} must be host-admin only.");
        }
    }

    [Fact]
    public async Task Host_admin_probe_reports_the_flag_and_revoking_it_closes_the_console_immediately()
    {
        var subject = TestData.NewSubject();
        var (register, _) = await fixture.AuthClient(subject).RegisterOrgAsync(TestData.NewSlug("hprobe"));
        register.EnsureSuccessStatusCode();

        var client = fixture.AuthClient(subject);
        (await client.GetFromJsonAsync<HostAdminProbe>("/api/v1/users/me/host-admin"))!.IsHostAdmin.ShouldBeFalse();

        await MakeHostAdminAsync(subject);
        (await client.GetFromJsonAsync<HostAdminProbe>("/api/v1/users/me/host-admin"))!.IsHostAdmin.ShouldBeTrue();
        (await client.GetAsync(new Uri("/api/v1/host/overview", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The policy is evaluated per request, so a revoke needs no token refresh or re-login.
        await SetHostAdminFlagAsync(fixture, subject, false);
        (await client.GetFromJsonAsync<HostAdminProbe>("/api/v1/users/me/host-admin"))!.IsHostAdmin.ShouldBeFalse();
        (await client.GetAsync(new Uri("/api/v1/host/overview", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---- cross-workspace visibility (the RLS policies) ----

    [Fact]
    public async Task Host_admin_sees_a_workspace_they_are_not_a_member_of()
    {
        var (host, _, _, _, strangerSubject, strangerWorkspace) = await SetupAsync("hsee");

        var page = await host.GetFromJsonAsync<HostPage<HostWorkspaceSummary>>(
            $"/api/v1/host/workspaces?search={strangerWorkspace.Slug}");

        var found = page!.Items.ShouldHaveSingleItem();
        found.Id.ShouldBe(strangerWorkspace.Id);
        found.Status.ShouldBe("Active");
        found.MemberCount.ShouldBe(1);
        found.OwnerEmail.ShouldBe($"{strangerSubject}@planvexa.test");

        var detail = await host.GetFromJsonAsync<HostWorkspaceDetail>($"/api/v1/host/workspaces/{strangerWorkspace.Id}");
        detail!.Summary.Slug.ShouldBe(strangerWorkspace.Slug);
        detail.Members.ShouldHaveSingleItem().Role.ShouldBe("Owner");
        detail.EnabledFeatures.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Host_admin_sees_users_and_their_memberships_across_workspaces()
    {
        var (host, _, _, stranger, strangerSubject, strangerWorkspace) = await SetupAsync("husers");
        var (_, memberUserId) = await fixture.InviteMemberAsync(stranger, strangerWorkspace.Id, "hu");

        var page = await host.GetFromJsonAsync<HostPage<HostUserSummary>>(
            $"/api/v1/host/users?search={strangerSubject}");
        var summary = page!.Items.ShouldHaveSingleItem();
        summary.IsActive.ShouldBeTrue();
        summary.IsHostAdmin.ShouldBeFalse();
        summary.WorkspaceCount.ShouldBe(1);

        var detail = await host.GetFromJsonAsync<HostUserDetail>($"/api/v1/host/users/{memberUserId}");
        detail!.Memberships.ShouldHaveSingleItem().WorkspaceId.ShouldBe(strangerWorkspace.Id);
        detail.Memberships[0].Role.ShouldBe("Member");
    }

    [Fact]
    public async Task Host_admin_sees_usage_counts_but_no_workspace_content()
    {
        var (host, _, _, stranger, _, strangerWorkspace) = await SetupAsync("husage");

        // Two tasks with recognisable titles: the assertion below is that no host response ever carries
        // them, which is the metadata-only boundary this console is built on.
        var space = await stranger.CreateSpaceAsync();
        var list = await stranger.CreateListAsync(space.Id);
        await stranger.CreateTaskAsync(list.Id, "TOP-SECRET-ALPHA");
        await stranger.CreateTaskAsync(list.Id, "TOP-SECRET-BETA");

        var usage = await host.GetFromJsonAsync<HostWorkspaceUsage>(
            $"/api/v1/host/workspaces/{strangerWorkspace.Id}/usage");
        usage!.Tasks.ShouldBe(2);
        usage.Spaces.ShouldBeGreaterThan(0);

        foreach (var route in new[]
                 {
                     "/api/v1/host/overview",
                     $"/api/v1/host/workspaces?search={strangerWorkspace.Slug}",
                     $"/api/v1/host/workspaces/{strangerWorkspace.Id}",
                     $"/api/v1/host/workspaces/{strangerWorkspace.Id}/usage",
                     "/api/v1/host/activity",
                 })
        {
            var body = await host.GetStringAsync(new Uri(route, UriKind.Relative));
            body.ShouldNotContain("TOP-SECRET", Case.Insensitive,
                $"{route} leaked workspace content; host administration is metadata-only.");
        }
    }

    // ---- workspace suspension ----

    [Fact]
    public async Task Suspending_a_workspace_locks_its_members_out_and_restoring_lets_them_back_in()
    {
        var (host, _, _, stranger, _, strangerWorkspace) = await SetupAsync("hsusp");

        (await stranger.GetAsync(new Uri("/api/v1/features", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var suspend = await host.PostAsync(
            new Uri($"/api/v1/host/workspaces/{strangerWorkspace.Id}/suspend", UriKind.Relative), null);
        suspend.StatusCode.ShouldBe(HttpStatusCode.OK);

        // WorkspaceResolutionMiddleware refuses any workspace whose status is not Active, so the
        // lockout needs no new enforcement anywhere — Archived already meant "nobody may enter".
        (await stranger.GetAsync(new Uri("/api/v1/features", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var listed = await host.GetFromJsonAsync<HostPage<HostWorkspaceSummary>>(
            $"/api/v1/host/workspaces?search={strangerWorkspace.Slug}");
        listed!.Items.ShouldHaveSingleItem().Status.ShouldBe("Archived");

        (await host.PostAsync(new Uri($"/api/v1/host/workspaces/{strangerWorkspace.Id}/restore", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await stranger.GetAsync(new Uri("/api/v1/features", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Host_admin_deletes_a_workspace_they_do_not_belong_to_after_retyping_its_slug()
    {
        var (host, _, _, stranger, _, strangerWorkspace) = await SetupAsync("hdel");

        var wrongSlug = await host.PostAsJsonAsync(
            $"/api/v1/host/workspaces/{strangerWorkspace.Id}/delete", new { confirmSlug = "not-the-slug" });
        wrongSlug.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var deleted = await host.PostAsJsonAsync(
            $"/api/v1/host/workspaces/{strangerWorkspace.Id}/delete", new { confirmSlug = strangerWorkspace.Slug });
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var listed = await host.GetFromJsonAsync<HostPage<HostWorkspaceSummary>>(
            $"/api/v1/host/workspaces?search={strangerWorkspace.Slug}");
        listed!.Items.ShouldBeEmpty();

        (await stranger.GetAsync(new Uri("/api/v1/features", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---- account suspension ----

    [Fact]
    public async Task Disabling_an_account_blocks_its_very_next_request_and_enabling_restores_it()
    {
        var (host, _, _, stranger, strangerSubject, strangerWorkspace) = await SetupAsync("hdisable");

        var page = await host.GetFromJsonAsync<HostPage<HostUserSummary>>(
            $"/api/v1/host/users?search={strangerSubject}");
        var target = page!.Items.ShouldHaveSingleItem();

        (await host.PostAsync(new Uri($"/api/v1/host/users/{target.Id}/disable", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Enforced in UserDirectory.GetOrProvisionAsync, which every authenticated request passes
        // through — so this holds for bootstrap endpoints too, not just workspace-scoped ones.
        (await stranger.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.AuthClient(strangerSubject).GetAsync(new Uri("/api/v1/workspaces/me", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await host.PostAsync(new Uri($"/api/v1/host/users/{target.Id}/enable", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.AuthClient(strangerSubject, strangerWorkspace.Id)
            .GetAsync(new Uri("/api/v1/users/me", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Host_admin_cannot_disable_or_demote_themselves()
    {
        var (host, _, hostUserId, _, _, _) = await SetupAsync("hself");

        (await host.PostAsync(new Uri($"/api/v1/host/users/{hostUserId}/disable", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await host.PostAsJsonAsync($"/api/v1/host/users/{hostUserId}/host-admin", new { granted = false }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Still in.
        (await host.GetAsync(new Uri("/api/v1/host/overview", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Host_admin_promotes_and_demotes_another_account()
    {
        var (host, _, _, _, strangerSubject, _) = await SetupAsync("hpromote");

        var page = await host.GetFromJsonAsync<HostPage<HostUserSummary>>(
            $"/api/v1/host/users?search={strangerSubject}");
        var target = page!.Items.ShouldHaveSingleItem();
        target.IsHostAdmin.ShouldBeFalse();

        (await host.PostAsJsonAsync($"/api/v1/host/users/{target.Id}/host-admin", new { granted = true }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var promoted = fixture.AuthClient(strangerSubject);
        (await promoted.GetAsync(new Uri("/api/v1/host/overview", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await host.PostAsJsonAsync($"/api/v1/host/users/{target.Id}/host-admin", new { granted = false }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await promoted.GetAsync(new Uri("/api/v1/host/overview", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_disabled_account_cannot_be_made_a_host_admin()
    {
        var (host, _, _, _, strangerSubject, _) = await SetupAsync("hdisabledpromote");

        var page = await host.GetFromJsonAsync<HostPage<HostUserSummary>>(
            $"/api/v1/host/users?search={strangerSubject}");
        var target = page!.Items.ShouldHaveSingleItem();

        (await host.PostAsync(new Uri($"/api/v1/host/users/{target.Id}/disable", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await host.PostAsJsonAsync($"/api/v1/host/users/{target.Id}/host-admin", new { granted = true }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // ---- audit ----

    [Fact]
    public async Task Workspace_targeted_host_actions_are_audited_against_that_workspace()
    {
        var (host, _, hostUserId, _, _, strangerWorkspace) = await SetupAsync("hauditws");

        (await host.PostAsync(new Uri($"/api/v1/host/workspaces/{strangerWorkspace.Id}/suspend", UriKind.Relative), null))
            .EnsureSuccessStatusCode();

        var activity = await host.GetFromJsonAsync<HostPage<HostActivityEntry>>(
            "/api/v1/host/activity?action=host.workspace.suspended");

        activity!.Items.ShouldContain(e => e.EntityId == strangerWorkspace.Id);
        var entry = activity.Items.First(e => e.EntityId == strangerWorkspace.Id);
        entry.ActorUserId.ShouldBe(hostUserId);
        // Carries the target workspace, not null: suspending binds the scope to that workspace (RLS
        // authorizes the status UPDATE through it), and audit_isolation's WITH CHECK would reject a
        // null row written under a non-null ambient workspace. The `host.` action prefix — not a null
        // workspace — is what marks this as an instance-level action.
        entry.WorkspaceId.ShouldBe(strangerWorkspace.Id);
    }

    [Fact]
    public async Task Account_targeted_host_actions_are_audited_as_platform_level_events_with_no_workspace()
    {
        var (host, _, hostUserId, _, strangerSubject, _) = await SetupAsync("hauditu");

        var page = await host.GetFromJsonAsync<HostPage<HostUserSummary>>(
            $"/api/v1/host/users?search={strangerSubject}");
        var target = page!.Items.ShouldHaveSingleItem();

        (await host.PostAsync(new Uri($"/api/v1/host/users/{target.Id}/disable", UriKind.Relative), null))
            .EnsureSuccessStatusCode();

        var activity = await host.GetFromJsonAsync<HostPage<HostActivityEntry>>(
            "/api/v1/host/activity?action=host.user.disabled");

        activity!.Items.ShouldContain(e => e.EntityId == target.Id);
        var entry = activity.Items.First(e => e.EntityId == target.Id);
        entry.ActorUserId.ShouldBe(hostUserId);
        // Disabling an account is about no Workspace in particular and runs with none ambient, so
        // AuditWriter stamps null — the documented meaning of the column for platform-level events.
        entry.WorkspaceId.ShouldBeNull();
    }

    [Fact]
    public async Task Activity_exports_as_csv_with_spreadsheet_formulas_neutralised()
    {
        // A display name that Excel would otherwise execute as a formula when the export is opened.
        var hostSubject = TestData.NewSubject();
        var hostClient = fixture.AuthClient(hostSubject);
        hostClient.DefaultRequestHeaders.Remove("X-Debug-Name");
        hostClient.DefaultRequestHeaders.Add("X-Debug-Name", "=cmd|'/c calc'!A1");
        var (register, _) = await hostClient.RegisterOrgAsync(TestData.NewSlug("hcsv"));
        register.EnsureSuccessStatusCode();
        await MakeHostAdminAsync(hostSubject);

        var (_, _, _, _, _, target) = await SetupAsync("hcsvt");
        (await hostClient.PostAsync(
            new Uri($"/api/v1/host/workspaces/{target.Id}/suspend", UriKind.Relative), null)).EnsureSuccessStatusCode();

        var response = await hostClient.GetAsync(
            new Uri("/api/v1/host/activity/export?action=host.workspace.suspended", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/csv");

        var csv = await response.Content.ReadAsStringAsync();
        csv.ShouldStartWith("When (UTC),Action,Entity type");
        csv.ShouldContain("host.workspace.suspended");
        // Apostrophe-prefixed: spreadsheets read it as text instead of running it.
        csv.ShouldContain("'=cmd");
        csv.ShouldNotContain(",=cmd");
    }

    [Fact]
    public async Task Activity_export_is_host_admin_only()
    {
        var (_, _, _, stranger, _, _) = await SetupAsync("hcsvdeny");

        (await stranger.GetAsync(new Uri("/api/v1/host/activity/export", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---- health & logs ----

    [Fact]
    public async Task Health_reports_the_database_the_schema_version_and_the_log_configuration()
    {
        var (host, _, _, _, _, _) = await SetupAsync("hhealth");

        var health = await host.GetFromJsonAsync<InstanceHealthResponse>("/api/v1/host/health");

        health!.DatabaseReachable.ShouldBeTrue();
        health.DatabaseVersion.ShouldNotBeNullOrWhiteSpace();
        // DbUp's own journal, so this tracks the real deployed schema rather than a hardcoded number.
        health.AppliedScripts.ShouldBeGreaterThan(0);
        health.LatestScript.ShouldNotBeNullOrWhiteSpace();
        health.LogMinimumLevel.ShouldNotBeNullOrWhiteSpace();
        health.LogRetentionDays.ShouldBeGreaterThan(0);
        health.Environment.ShouldBe("Testing");
        health.OutboxPending.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Log_search_is_host_admin_only_and_pages()
    {
        var (host, _, _, stranger, _, _) = await SetupAsync("hlogs");

        (await stranger.GetAsync(new Uri("/api/v1/host/logs", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Capture is asynchronous (a channel drained on a timer), so this asserts the endpoint's
        // contract — filters apply, paging is bounded — rather than racing the writer for content.
        var page = await host.GetFromJsonAsync<HostPage<InstanceLogResponse>>(
            "/api/v1/host/logs?level=Warning&take=5");
        page.ShouldNotBeNull();
        page.Items.Count.ShouldBeLessThanOrEqualTo(5);
        page.Total.ShouldBeGreaterThanOrEqualTo(page.Items.Count);

        // "Warning" means warning-and-worse: an Information record must never come back from it.
        page.Items.ShouldAllBe(e => e.Level == "Warning" || e.Level == "Error" || e.Level == "Critical");
    }

    // ---- instance settings ----

    [Fact]
    public async Task Host_admin_edits_instance_settings_and_the_anonymous_endpoint_reflects_them()
    {
        var (host, _, hostUserId, _, _, _) = await SetupAsync("hsettings");

        var updated = await host.PutAsJsonAsync("/api/v1/host/settings", new
        {
            instanceName = "Acme Internal",
            supportEmail = "Help@Acme.Example",
            logoUrl = "https://acme.example/logo.png",
        });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);

        var settings = await host.GetFromJsonAsync<InstanceSettingsResponse>("/api/v1/host/settings");
        settings!.InstanceName.ShouldBe("Acme Internal");
        settings.SupportEmail.ShouldBe("help@acme.example");
        settings.UpdatedByUserId.ShouldBe(hostUserId);

        // Anonymous: the sign-in page reads this before there is a session, so branding must reach it.
        var anonymous = fixture.Factory.CreateClient();
        var policy = await anonymous.GetFromJsonAsync<PublicPolicyResponse>("/api/v1/public/registration-policy");
        policy!.InstanceName.ShouldBe("Acme Internal");
        policy.SupportEmail.ShouldBe("help@acme.example");

        // Restore, so the shared fixture's other tests see the defaults they expect.
        (await host.PutAsJsonAsync("/api/v1/host/settings", new
        {
            instanceName = "", supportEmail = "", logoUrl = "",
        })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Instance_settings_reject_an_unknown_policy_and_a_non_http_logo_url()
    {
        var (host, _, _, _, _, _) = await SetupAsync("hsettingsbad");

        (await host.PutAsJsonAsync("/api/v1/host/settings", new { workspaceCreationPolicy = "Whoever" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Rendered in an <img src> on the anonymous sign-in page — a javascript:/data: URL must not be
        // storable there.
        (await host.PutAsJsonAsync("/api/v1/host/settings", new { logoUrl = "javascript:alert(1)" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await host.PutAsJsonAsync("/api/v1/host/settings", new { supportEmail = "not-an-email" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The self-registration toggle end to end: flipped through the host console, enforced on the very
    /// next request by a brand-new identity, and reflected by the anonymous endpoint the sign-in page
    /// reads. RegistrationGateTests covers the same gate seeded from CONFIGURATION; this covers the
    /// live toggle, which is the path an operator actually uses.
    /// </summary>
    [Fact]
    public async Task Turning_off_self_registration_blocks_new_identities_and_shows_on_the_public_endpoint()
    {
        var (host, _, _, _, _, _) = await SetupAsync("hselfreg");

        // Baseline: an unknown subject can provision itself.
        (await fixture.AuthClient(TestData.NewSubject()).GetAsync(new Uri("/api/v1/users/me", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await host.PutAsJsonAsync("/api/v1/host/settings", new { allowSelfRegistration = false }))
            .EnsureSuccessStatusCode();

        try
        {
            // Enforced in UserDirectory.GetOrProvisionAsync — the path EVERY authenticated request takes,
            // so this holds for bootstrap endpoints too, not just workspace-scoped ones. No restart and
            // no cache wait: InstanceSettingsService invalidates its memo on write.
            (await fixture.AuthClient(TestData.NewSubject()).GetAsync(new Uri("/api/v1/users/me", UriKind.Relative)))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            // An account that already exists is unaffected — closing registration must not lock out the
            // people already using the instance.
            (await host.GetAsync(new Uri("/api/v1/host/overview", UriKind.Relative)))
                .StatusCode.ShouldBe(HttpStatusCode.OK);

            // And the anonymous endpoint the landing/sign-in pages read reports it, so they can stop
            // offering a signup path that would only be rejected.
            var anonymous = fixture.Factory.CreateClient();
            var policy = await anonymous.GetFromJsonAsync<PublicPolicyResponse>("/api/v1/public/registration-policy");
            policy!.AllowSelfRegistration.ShouldBeFalse();
        }
        finally
        {
            (await host.PutAsJsonAsync("/api/v1/host/settings", new { allowSelfRegistration = true }))
                .EnsureSuccessStatusCode();
        }

        // Turning it back on re-opens registration immediately.
        (await fixture.AuthClient(TestData.NewSubject()).GetAsync(new Uri("/api/v1/users/me", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The invitation escape hatch has to keep working with self-registration off, or closing
    /// registration would also break inviting new people — which is the main reason to close it.
    /// </summary>
    [Fact]
    public async Task An_invited_person_can_still_join_while_self_registration_is_off()
    {
        var (host, _, _, stranger, _, strangerWorkspace) = await SetupAsync("hselfreginv");

        (await host.PutAsJsonAsync("/api/v1/host/settings", new { allowSelfRegistration = false }))
            .EnsureSuccessStatusCode();

        try
        {
            // InviteMemberAsync provisions a brand-new subject and accepts the invitation — exactly the
            // flow the gate must let through on the strength of the pending invitation alone.
            var (_, invitedUserId) = await fixture.InviteMemberAsync(stranger, strangerWorkspace.Id, "selfreg-inv");
            invitedUserId.ShouldNotBe(Guid.Empty);
        }
        finally
        {
            (await host.PutAsJsonAsync("/api/v1/host/settings", new { allowSelfRegistration = true }))
                .EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Restricting_workspace_creation_to_host_admins_blocks_everyone_else()
    {
        var (host, hostSubject, _, _, _, _) = await SetupAsync("hwscreate");

        (await host.PutAsJsonAsync("/api/v1/host/settings", new { workspaceCreationPolicy = "HostAdminsOnly" }))
            .EnsureSuccessStatusCode();

        try
        {
            var ordinary = fixture.AuthClient(TestData.NewSubject());
            var (blocked, _) = await ordinary.RegisterOrgAsync(TestData.NewSlug("hwsblock"));
            blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            // Enforced in WorkspaceRegistrationService — the single creation path — so the host admin
            // themselves is still allowed through the very same code.
            var (allowed, _) = await fixture.AuthClient(hostSubject).RegisterOrgAsync(TestData.NewSlug("hwsallow"));
            allowed.StatusCode.ShouldBe(HttpStatusCode.Created);
        }
        finally
        {
            (await host.PutAsJsonAsync("/api/v1/host/settings", new { workspaceCreationPolicy = "Anyone" }))
                .EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Overview_counts_the_instance_not_the_callers_own_workspaces()
    {
        var (host, _, _, _, _, _) = await SetupAsync("hoverview");

        var overview = await host.GetFromJsonAsync<HostOverview>("/api/v1/host/overview");

        // The host admin belongs to exactly one workspace but must see at least their own plus the
        // stranger's — proof the counts are instance-wide rather than membership-scoped.
        overview!.ActiveWorkspaces.ShouldBeGreaterThanOrEqualTo(2);
        overview.ActiveUsers.ShouldBeGreaterThanOrEqualTo(2);
        overview.HostAdmins.ShouldBeGreaterThanOrEqualTo(1);
        overview.Memberships.ShouldBeGreaterThanOrEqualTo(2);
        overview.RecentActivity.ShouldNotBeEmpty();
    }
}
