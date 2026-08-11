namespace Planvexa.IntegrationTests;

using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

[Collection("api")]
public sealed class RealtimeTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Two_clients_receive_entity_changed_when_one_edits_a_task()
    {
        var (owner, workspaceId, _, ownerSubject) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "rt");

        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "Realtime task");

        await using var ownerHub = BuildHub(ownerSubject);
        await using var memberHub = BuildHub(memberSubject);

        var received = new TaskCompletionSource<RealtimeEventDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        memberHub.On<RealtimeEventDto>("entityChanged", evt =>
        {
            if (evt.EntityId == task.Id)
            {
                received.TrySetResult(evt);
            }
        });

        await ownerHub.StartAsync();
        await memberHub.StartAsync();
        await ownerHub.InvokeAsync("JoinWorkspace", workspaceId);
        await memberHub.InvokeAsync("JoinWorkspace", workspaceId);

        // The owner completes the task; the member's client should receive the realtime signal.
        (await owner.PostAsync(new Uri($"/api/v1/tasks/{task.Id}/complete", UriKind.Relative), null))
            .EnsureSuccessStatusCode();

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.ShouldBe(received.Task, "the member should have received an entityChanged event");

        var evt = await received.Task;
        evt.WorkspaceId.ShouldBe(workspaceId);
        evt.EntityType.ShouldBe("Task");
        evt.Action.ShouldBe("completed");
    }

    [Fact]
    public async Task Two_clients_receive_entity_changed_when_one_starts_a_timer()
    {
        var (owner, workspaceId, _, ownerSubject) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "rttimer");

        await using var ownerHub = BuildHub(ownerSubject);
        await using var memberHub = BuildHub(memberSubject);

        var received = new TaskCompletionSource<RealtimeEventDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        memberHub.On<RealtimeEventDto>("entityChanged", evt =>
        {
            if (evt.EntityType == "TimeEntry")
            {
                received.TrySetResult(evt);
            }
        });

        await ownerHub.StartAsync();
        await memberHub.StartAsync();
        await ownerHub.InvokeAsync("JoinWorkspace", workspaceId);
        await memberHub.InvokeAsync("JoinWorkspace", workspaceId);

        (await owner.PostAsJsonAsync("/api/v1/timers/start", new { description = "realtime timer" }))
            .EnsureSuccessStatusCode();

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.ShouldBe(received.Task, "the member should have received a TimeEntry entityChanged event");

        var evt = await received.Task;
        evt.WorkspaceId.ShouldBe(workspaceId);
        evt.Action.ShouldBe("started");
    }

    [Fact]
    public async Task Presence_reflects_joined_users()
    {
        var (owner, workspaceId, _, ownerSubject) = await fixture.NewWorkspaceClientAsync();

        await using var hub = BuildHub(ownerSubject);
        await hub.StartAsync();
        await hub.InvokeAsync("JoinWorkspace", workspaceId);

        // Presence snapshot (via the REST endpoint) includes the joined user.
        var presence = await owner.GetFromJsonAsync<PresenceResp>($"/api/v1/workspaces/{workspaceId}/presence");
        presence!.UserIds.Count.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Joining_a_workspace_without_a_debug_workspace_header_works()
    {
        var (owner, workspaceId, _, ownerSubject) = await fixture.NewWorkspaceClientAsync();

        await using var hub = BuildHub(ownerSubject);
        await hub.StartAsync();
        await hub.InvokeAsync("JoinWorkspace", workspaceId);

        var presence = await owner.GetFromJsonAsync<PresenceResp>($"/api/v1/workspaces/{workspaceId}/presence");
        presence!.UserIds.Count.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Typing_signal_reaches_other_group_members_but_not_the_sender()
    {
        var (owner, workspaceId, _, ownerSubject) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "typing");

        await using var ownerHub = BuildHub(ownerSubject);
        await using var memberHub = BuildHub(memberSubject);

        var taskId = Guid.NewGuid();
        var memberReceived = new TaskCompletionSource<TypingEventDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var senderReceivedAnything = false;
        memberHub.On<TypingEventDto>("typing", evt => memberReceived.TrySetResult(evt));
        ownerHub.On<TypingEventDto>("typing", _ => senderReceivedAnything = true);

        await ownerHub.StartAsync();
        await memberHub.StartAsync();
        await ownerHub.InvokeAsync("JoinWorkspace", workspaceId);
        await memberHub.InvokeAsync("JoinWorkspace", workspaceId);

        await ownerHub.InvokeAsync("Typing", workspaceId, "Task", taskId);

        var completed = await Task.WhenAny(memberReceived.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.ShouldBe(memberReceived.Task, "the other group member should have received the typing signal");

        var evt = await memberReceived.Task;
        evt.WorkspaceId.ShouldBe(workspaceId);
        evt.ResourceType.ShouldBe("Task");
        evt.ResourceId.ShouldBe(taskId);
        evt.ExpiresAtUtc.ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        senderReceivedAnything.ShouldBeFalse("the sender's own client should not be echoed the typing signal");
    }

    [Fact]
    public async Task Joining_a_workspace_without_access_is_rejected()
    {
        var (_, _, _, _) = await fixture.NewWorkspaceClientAsync();
        // A second org whose owner is NOT a member of the first workspace.
        var (owner2, workspace2, _, subject2) = await fixture.NewWorkspaceClientAsync();
        _ = owner2;

        await using var hub = BuildHub(subject2);
        await hub.StartAsync();

        // Joining a random workspace id the user has no access to must throw.
        await Should.ThrowAsync<HubException>(async () => await hub.InvokeAsync("JoinWorkspace", Guid.NewGuid()));
        _ = workspace2;
    }

    private HubConnection BuildHub(string subject)
    {
        var url = "http://localhost/hubs/workspace";

        return new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.HttpMessageHandlerFactory = _ => fixture.Factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers["X-Debug-Subject"] = subject;
                options.Headers["X-Debug-Email"] = $"{subject}@planvexa.test";
            })
            .Build();
    }

    private sealed record RealtimeEventDto(
        Guid WorkspaceId, string EntityType, Guid EntityId, string Action, long? Version, string CorrelationId);

    private sealed record PresenceResp(List<Guid> UserIds);

    private sealed record TypingEventDto(Guid WorkspaceId, string ResourceType, Guid ResourceId, Guid UserId, DateTimeOffset ExpiresAtUtc);
}
