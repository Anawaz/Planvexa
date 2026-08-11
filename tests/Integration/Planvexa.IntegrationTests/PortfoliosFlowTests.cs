namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

// Note: PortfolioResp (the rollup row shape) is already declared in PlanningFlowTests.cs -- reused here.
internal sealed record CuratedPortfolioResp(
    Guid Id, string Name, Guid OwnerUserId, bool IsPrivate, string Status,
    DateTimeOffset? StartUtc, DateTimeOffset? TargetEndUtc, List<Guid> SpaceIds);

/// <summary>
/// Curated Portfolios: named, owned groups of a chosen subset of the workspace's Spaces, with their own
/// scoped Health rollup (PortfolioService.GetReportAsync) -- distinct from the pre-existing workspace-wide
/// GET /reports/portfolio (kept for backward compatibility, see PortfolioService's doc comment). Load
/// bearing test is the scoping one: a curated portfolio's rollup must include only its own Spaces, never
/// every Space in the workspace (AGENTS.md rule 11 -- negative/cross-permission tests are mandatory).
/// </summary>
[Collection("api")]
public sealed class PortfoliosFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Portfolio_rollup_is_scoped_to_only_its_curated_spaces()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var spaceA = await owner.CreateSpaceAsync("Space A");
        var listA = await owner.CreateListAsync(spaceA.Id);
        await owner.CreateTaskAsync(listA.Id, "A task");

        var spaceB = await owner.CreateSpaceAsync("Space B");
        var listB = await owner.CreateListAsync(spaceB.Id);
        await owner.CreateTaskAsync(listB.Id, "B task 1");
        await owner.CreateTaskAsync(listB.Id, "B task 2");

        var spaceC = await owner.CreateSpaceAsync("Space C");
        var listC = await owner.CreateListAsync(spaceC.Id);
        await owner.CreateTaskAsync(listC.Id, "C task");

        var create = await owner.PostAsJsonAsync("/api/v1/portfolios", new
        {
            name = "Curated portfolio",
            isPrivate = false,
            status = "OnTrack",
            spaceIds = new[] { spaceA.Id, spaceB.Id },
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var portfolio = (await create.Content.ReadFromJsonAsync<CuratedPortfolioResp>())!;
        portfolio.SpaceIds.ShouldBe(new[] { spaceA.Id, spaceB.Id }, ignoreOrder: true);

        var report = (await owner.GetFromJsonAsync<List<PortfolioResp>>($"/api/v1/portfolios/{portfolio.Id}"))!;
        report.Select(r => r.Key).ShouldBe(new[] { spaceA.Id.ToString(), spaceB.Id.ToString() }, ignoreOrder: true);
        report.ShouldNotContain(r => r.Key == spaceC.Id.ToString());
        report.Single(r => r.Key == spaceB.Id.ToString()).TotalTasks.ShouldBe(2);
    }

    [Fact]
    public async Task Portfolio_CRUD_round_trips()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space1 = await owner.CreateSpaceAsync("Space 1");
        var space2 = await owner.CreateSpaceAsync("Space 2");

        var create = await owner.PostAsJsonAsync("/api/v1/portfolios", new
        {
            name = "Q1 portfolio",
            isPrivate = false,
            status = "OnTrack",
            spaceIds = new[] { space1.Id },
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var portfolio = (await create.Content.ReadFromJsonAsync<CuratedPortfolioResp>())!;

        var listed = await owner.GetFromJsonAsync<List<CuratedPortfolioResp>>("/api/v1/portfolios");
        listed!.ShouldContain(p => p.Id == portfolio.Id && p.Name == "Q1 portfolio");

        var update = await owner.PatchAsJsonAsync($"/api/v1/portfolios/{portfolio.Id}", new
        {
            name = "Q1 portfolio (renamed)",
            status = "AtRisk",
            spaceIds = new[] { space1.Id, space2.Id },
        });
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = (await update.Content.ReadFromJsonAsync<CuratedPortfolioResp>())!;
        updated.Name.ShouldBe("Q1 portfolio (renamed)");
        updated.Status.ShouldBe("AtRisk");
        updated.SpaceIds.ShouldBe(new[] { space1.Id, space2.Id }, ignoreOrder: true);

        var delete = await owner.DeleteAsync(new Uri($"/api/v1/portfolios/{portfolio.Id}", UriKind.Relative));
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterDelete = await owner.GetFromJsonAsync<List<CuratedPortfolioResp>>("/api/v1/portfolios");
        afterDelete!.ShouldNotContain(p => p.Id == portfolio.Id);
    }

    [Fact]
    public async Task Workspace_B_cannot_read_or_edit_workspace_As_portfolio()
    {
        var (ownerA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var spaceA = await ownerA.CreateSpaceAsync("A space");
        var create = await ownerA.PostAsJsonAsync("/api/v1/portfolios", new
        {
            name = "A-only portfolio",
            isPrivate = false,
            status = "OnTrack",
            spaceIds = new[] { spaceA.Id },
        });
        create.EnsureSuccessStatusCode();
        var portfolio = (await create.Content.ReadFromJsonAsync<CuratedPortfolioResp>())!;

        var (ownerB, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var listed = await ownerB.GetFromJsonAsync<List<CuratedPortfolioResp>>("/api/v1/portfolios");
        listed!.ShouldNotContain(p => p.Id == portfolio.Id);

        var report = await ownerB.GetAsync(new Uri($"/api/v1/portfolios/{portfolio.Id}", UriKind.Relative));
        report.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var update = await ownerB.PatchAsJsonAsync($"/api/v1/portfolios/{portfolio.Id}", new { name = "Hijacked" });
        update.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var delete = await ownerB.DeleteAsync(new Uri($"/api/v1/portfolios/{portfolio.Id}", UriKind.Relative));
        delete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
