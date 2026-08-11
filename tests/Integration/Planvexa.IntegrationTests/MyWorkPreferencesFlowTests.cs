namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

internal sealed record MyWorkPreferencesResp(string SortBy, List<string> HiddenSections);

/// <summary>My Work personal sort/organize preferences (product spec section 15) via the real API — a
/// global, per-user row (not per-Workspace: see MyWorkPreference's doc comment), so the interesting
/// negative case here is cross-USER isolation rather than cross-Workspace.</summary>
[Collection("api")]
public sealed class MyWorkPreferencesFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task No_saved_preference_returns_the_default()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var prefs = await client.GetFromJsonAsync<MyWorkPreferencesResp>("/api/v1/tasks/mine/preferences");

        prefs!.SortBy.ShouldBe("dueDate");
        prefs.HiddenSections.ShouldBeEmpty();
    }

    [Fact]
    public async Task Saving_preferences_persists_them_for_later_reads()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var putResponse = await client.PutAsJsonAsync(
            "/api/v1/tasks/mine/preferences", new { sortBy = "priority", hiddenSections = new[] { "watching" } });
        putResponse.EnsureSuccessStatusCode();

        var prefs = await client.GetFromJsonAsync<MyWorkPreferencesResp>("/api/v1/tasks/mine/preferences");
        prefs!.SortBy.ShouldBe("priority");
        prefs.HiddenSections.ShouldBe(["watching"]);
    }

    [Fact]
    public async Task Saving_again_overwrites_the_prior_choice_instead_of_duplicating()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        (await client.PutAsJsonAsync("/api/v1/tasks/mine/preferences", new { sortBy = "priority", hiddenSections = new[] { "watching" } }))
            .EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/api/v1/tasks/mine/preferences", new { sortBy = "title", hiddenSections = Array.Empty<string>() }))
            .EnsureSuccessStatusCode();

        var prefs = await client.GetFromJsonAsync<MyWorkPreferencesResp>("/api/v1/tasks/mine/preferences");
        prefs!.SortBy.ShouldBe("title");
        prefs.HiddenSections.ShouldBeEmpty();
    }

    [Fact]
    public async Task Preferences_are_global_to_the_user_not_scoped_to_one_workspace()
    {
        var (client, _, _, subject) = await fixture.NewWorkspaceClientAsync();
        (await client.PutAsJsonAsync("/api/v1/tasks/mine/preferences", new { sortBy = "priority", hiddenSections = Array.Empty<string>() }))
            .EnsureSuccessStatusCode();

        // Same user, a second, unrelated Workspace — no X-Workspace header ties this call to the first one.
        var bootstrap = fixture.AuthClient(subject);
        var secondWorkspaceResponse = await bootstrap.PostAsJsonAsync(
            "/api/v1/workspaces", new { name = TestData.NewSlug("wm"), slug = TestData.NewSlug("wm") });
        secondWorkspaceResponse.EnsureSuccessStatusCode();
        var secondWorkspace = (await secondWorkspaceResponse.Content.ReadFromJsonAsync<WorkspaceResponse>())!;
        var clientInSecondWorkspace = fixture.WorkClient(subject, secondWorkspace.Id);

        var prefs = await clientInSecondWorkspace.GetFromJsonAsync<MyWorkPreferencesResp>("/api/v1/tasks/mine/preferences");
        prefs!.SortBy.ShouldBe("priority");
    }

    [Fact]
    public async Task A_users_preferences_are_never_visible_to_another_user()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        (await clientA.PutAsJsonAsync("/api/v1/tasks/mine/preferences", new { sortBy = "priority", hiddenSections = new[] { "created", "watching" } }))
            .EnsureSuccessStatusCode();

        var (clientB, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var prefsB = await clientB.GetFromJsonAsync<MyWorkPreferencesResp>("/api/v1/tasks/mine/preferences");

        prefsB!.SortBy.ShouldBe("dueDate");
        prefsB.HiddenSections.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_sort_value_is_rejected()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var response = await client.PutAsJsonAsync("/api/v1/tasks/mine/preferences", new { sortBy = "not-a-sort", hiddenSections = Array.Empty<string>() });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unknown_hidden_section_is_rejected()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var response = await client.PutAsJsonAsync("/api/v1/tasks/mine/preferences", new { sortBy = "dueDate", hiddenSections = new[] { "not-a-section" } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
