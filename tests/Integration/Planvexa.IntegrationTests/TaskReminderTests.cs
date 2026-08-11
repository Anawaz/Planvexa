namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

/// <summary>Per-user task reminders — create, list, delete, and ownership guard.</summary>
[Collection("api")]
public sealed class TaskReminderTests(PlanvexaFixture fixture)
{
    private sealed record ReminderResp(Guid Id, Guid TaskId, DateTimeOffset RemindAtUtc, string? Note, bool IsSent);

    [Fact]
    public async Task Reminder_can_be_created_listed_and_deleted()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Task with reminder");

        var when = DateTimeOffset.UtcNow.AddHours(2);
        var create = await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/reminders", new { remindAtUtc = when, note = "Ping me" });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var reminder = (await create.Content.ReadFromJsonAsync<ReminderResp>())!;
        reminder.Note.ShouldBe("Ping me");
        reminder.IsSent.ShouldBeFalse();

        var listed = await client.GetFromJsonAsync<List<ReminderResp>>($"/api/v1/tasks/{task.Id}/reminders");
        listed!.ShouldContain(r => r.Id == reminder.Id);

        (await client.DeleteAsync(new Uri($"/api/v1/reminders/{reminder.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterDelete = await client.GetFromJsonAsync<List<ReminderResp>>($"/api/v1/tasks/{task.Id}/reminders");
        afterDelete!.ShouldNotContain(r => r.Id == reminder.Id);
    }

    [Fact]
    public async Task A_member_cannot_delete_another_users_reminder()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "Owned task");

        var create = await owner.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/reminders",
            new { remindAtUtc = DateTimeOffset.UtcNow.AddHours(1), note = (string?)null });
        var reminder = (await create.Content.ReadFromJsonAsync<ReminderResp>())!;

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "rem");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        (await member.DeleteAsync(new Uri($"/api/v1/reminders/{reminder.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
