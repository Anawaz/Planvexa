namespace Planvexa.IntegrationTests;

using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Shouldly;
using Xunit;

// Response shapes for the governed-export endpoints (mirrors Application/Contracts.cs' ExportJobDto).
internal sealed record ExportJobResp(Guid Id, string Dataset, string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset? CompletedAtUtc, int? RowCount);

/// <summary>
/// The "full" governed workspace export — a zip archive of every entity type, built by
/// ExportRunner via IFileStorage instead of the flat inline-CSV path the "audit"/"tasks" datasets use.
/// </summary>
[Collection("api")]
public sealed class GovernanceFullExportFlowTests(PlanvexaFixture fixture)
{
    /// <summary>Polls an async predicate until it returns a non-null result or the timeout elapses (same
    /// pattern as the automation/webhook background-processing tests).</summary>
    private static async Task<T?> PollAsync<T>(Func<Task<T?>> probe, TimeSpan timeout) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = await probe();
            if (result is not null)
            {
                return result;
            }

            await Task.Delay(500);
        }

        return null;
    }

    private async Task<ExportJobResp> SeedWorkspaceAndCreateFullExportAsync(HttpClient client)
    {
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Exported task");

        (await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new { body = "A comment", parentId = (Guid?)null, mentionUserIds = (List<Guid>?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await client.PostAsJsonAsync("/api/v1/documents", new { title = "Doc", content = "hello", isPrivate = false, spaceId = space.Id, listId = (Guid?)null, taskId = (Guid?)null, parentDocumentId = (Guid?)null, templateId = (Guid?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var channelResponse = await client.PostAsJsonAsync("/api/v1/chat/channels", new { name = "general", description = (string?)null, isPrivate = false, memberUserIds = (List<Guid>?)null });
        channelResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var channel = (await channelResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>());
        var channelId = channel.GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync($"/api/v1/chat/channels/{channelId}/messages", new { parentMessageId = (Guid?)null, body = "Hi team", mentionUserIds = (List<Guid>?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await client.PostAsJsonAsync("/api/v1/time-entries", new { taskId = task.Id, startedAtUtc = DateTimeOffset.UtcNow.AddHours(-1), endedAtUtc = DateTimeOffset.UtcNow, durationSeconds = (long?)null, description = "Worked", isBillable = true, timeZoneId = "UTC", tagIds = (List<Guid>?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var fieldResponse = await client.PostAsJsonAsync("/api/v1/custom-fields", new { name = "Points", type = "Number", scope = "Workspace", scopeId = (Guid?)null, isRequired = false, options = (object?)null });
        fieldResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var field = (await fieldResponse.Content.ReadFromJsonAsync<CustomFieldResp>())!;
        (await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{field.Id}", new { value = "5" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var createExport = await client.PostAsJsonAsync("/api/v1/governance/exports", new { dataset = "full" });
        createExport.StatusCode.ShouldBe(HttpStatusCode.Created);
        var job = (await createExport.Content.ReadFromJsonAsync<ExportJobResp>())!;
        job.Dataset.ShouldBe("full");
        job.Status.ShouldBe("Pending");
        return job;
    }

    [Fact]
    public async Task Full_export_completes_with_a_valid_zip_containing_every_entity_and_expected_row_counts()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var job = await SeedWorkspaceAndCreateFullExportAsync(client);

        var completed = await PollAsync(async () =>
        {
            var found = await client.GetFromJsonAsync<ExportJobResp>($"/api/v1/governance/exports/{job.Id}");
            return found is { Status: "Completed" or "Failed" } ? found : null;
        }, TimeSpan.FromSeconds(30));

        completed.ShouldNotBeNull();
        completed!.Status.ShouldBe("Completed");
        completed.RowCount.ShouldNotBeNull();
        completed.RowCount!.Value.ShouldBeGreaterThanOrEqualTo(9); // workspace bootstrap seeds 1 space+1 list; +1 each of the rest seeded below

        var download = await client.GetAsync($"/api/v1/governance/exports/{job.Id}/download");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");

        var bytes = await download.Content.ReadAsByteArrayAsync();
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        var expectedFiles = new[]
        {
            "spaces.csv", "folders.csv", "lists.csv", "tasks.csv", "comments.csv",
            "documents.csv", "chat.csv", "timeEntries.csv", "customFieldDefinitions.csv", "customFieldValues.csv",
        };
        foreach (var name in expectedFiles)
        {
            zip.GetEntry(name).ShouldNotBeNull($"the archive should contain {name}");
        }

        // Workspace bootstrap (WorkspaceDefaultsProvisioner) seeds one "General" space + one "Tasks" list
        // before this test adds its own — so spaces/lists are 2, not 1.
        RowCountOf(zip, "spaces.csv").ShouldBe(2);
        RowCountOf(zip, "lists.csv").ShouldBe(2);
        RowCountOf(zip, "tasks.csv").ShouldBe(1);
        RowCountOf(zip, "comments.csv").ShouldBe(1);
        RowCountOf(zip, "documents.csv").ShouldBe(1);
        RowCountOf(zip, "chat.csv").ShouldBe(1);
        RowCountOf(zip, "timeEntries.csv").ShouldBe(1);
        RowCountOf(zip, "customFieldDefinitions.csv").ShouldBe(1);
        RowCountOf(zip, "customFieldValues.csv").ShouldBe(1);
    }

    [Fact]
    public async Task A_user_from_another_workspace_cannot_download_this_workspaces_export()
    {
        var (ownerClient, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var job = await SeedWorkspaceAndCreateFullExportAsync(ownerClient);

        var completed = await PollAsync(async () =>
        {
            var found = await ownerClient.GetFromJsonAsync<ExportJobResp>($"/api/v1/governance/exports/{job.Id}");
            return found is { Status: "Completed" or "Failed" } ? found : null;
        }, TimeSpan.FromSeconds(30));
        completed.ShouldNotBeNull();
        completed!.Status.ShouldBe("Completed");

        var (strangerClient, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var getAttempt = await strangerClient.GetAsync($"/api/v1/governance/exports/{job.Id}");
        getAttempt.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var downloadAttempt = await strangerClient.GetAsync($"/api/v1/governance/exports/{job.Id}/download");
        downloadAttempt.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static int RowCountOf(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName) ?? throw new InvalidOperationException($"Missing entry {entryName}");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var text = reader.ReadToEnd();
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        return lines.Length - 1; // minus the header row
    }
}
