namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

/// <summary>
/// Spec section 44 / AGENTS.md Phase 1B: "If a Workspace has MFA required, users subject to that
/// policy must actually be prevented from accessing the Workspace until MFA requirements are
/// satisfied" — enforced server-side in WorkspaceResolutionMiddleware, not only as a UI restriction.
/// The Dev auth handler's X-Debug-Amr header simulates the "amr" claim a real Keycloak token carries
/// once the OTP Form execution completes, so this is exercised without a running Keycloak.
/// </summary>
[Collection("api")]
public sealed class MfaEnforcementFlowTests(PlanvexaFixture fixture)
{
    private const string MfaRequiredProblemType = "https://planvexa.dev/problems/mfa-required";

    [Fact]
    public async Task Enabling_workspace_mfa_blocks_every_member_without_a_verified_second_factor_including_the_owner()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();

        // The owner completes MFA before turning the policy on, so enabling it doesn't lock them out
        // of the very call that enables it.
        owner.DefaultRequestHeaders.Add("X-Debug-Amr", "otp");
        var enable = await owner.PutAsJsonAsync("/api/v1/governance/security-settings", new { mfaRequired = true });
        enable.StatusCode.ShouldBe(HttpStatusCode.OK);

        // A regular member invited afterward, with no verified second factor, is blocked from every
        // workspace-scoped endpoint — not merely steered away from a UI option.
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "member");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        var blocked = await memberClient.GetAsync(new Uri("/api/v1/spaces", UriKind.Relative));
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("type").GetString().ShouldBe(MfaRequiredProblemType);

        // The same member, now carrying a verified "otp" factor, is let in.
        memberClient.DefaultRequestHeaders.Add("X-Debug-Amr", "otp");
        (await memberClient.GetAsync(new Uri("/api/v1/spaces", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The policy applies to the Owner too, once their session no longer carries a verified factor
        // (e.g. a later request/session that hasn't completed the OTP step) — Owner is not a carve-out.
        owner.DefaultRequestHeaders.Remove("X-Debug-Amr");
        var ownerBlocked = await owner.GetAsync(new Uri("/api/v1/spaces", UriKind.Relative));
        ownerBlocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_workspace_without_mfa_required_is_unaffected()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        (await client.GetAsync(new Uri("/api/v1/spaces", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Joining_the_realtime_hub_is_also_blocked_without_a_verified_second_factor()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();

        owner.DefaultRequestHeaders.Add("X-Debug-Amr", "otp");
        (await owner.PutAsJsonAsync("/api/v1/governance/security-settings", new { mfaRequired = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "rtmfa");

        await using var unverifiedHub = BuildHub(memberSubject, amr: null);
        await unverifiedHub.StartAsync();
        await Should.ThrowAsync<HubException>(async () => await unverifiedHub.InvokeAsync("JoinWorkspace", workspaceId));

        await using var verifiedHub = BuildHub(memberSubject, amr: "otp");
        await verifiedHub.StartAsync();
        await Should.NotThrowAsync(async () => await verifiedHub.InvokeAsync("JoinWorkspace", workspaceId));
    }

    private HubConnection BuildHub(string subject, string? amr)
    {
        return new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/workspace", options =>
            {
                options.HttpMessageHandlerFactory = _ => fixture.Factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers["X-Debug-Subject"] = subject;
                options.Headers["X-Debug-Email"] = $"{subject}@planvexa.test";
                if (amr is not null)
                {
                    options.Headers["X-Debug-Amr"] = amr;
                }
            })
            .Build();
    }
}
