namespace Planvexa.IntegrationTests;

using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

internal static class TestData
{
    public static string NewSlug(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 1 + 12, 30)];

    public static string NewSubject() => $"sub-{Guid.NewGuid():N}";
}

// Response shapes for deserialization (System.Text.Json Web defaults are case-insensitive).
internal sealed record WorkspaceResponse(Guid Id, string Name, string Slug, string Status, DateTimeOffset CreatedAtUtc);
internal sealed record MemberResponse(Guid Id, Guid UserId, string Role, string Status, bool IsGuest);
internal sealed record FeatureResponse(string Key, bool Enabled, long? Limit, string Source);
internal sealed record InvitationResponse(Guid InvitationId, string Email, string Role, DateTimeOffset ExpiresAtUtc);
internal sealed record AcceptResponse(Guid MembershipId, Guid WorkspaceId, string Role);

internal sealed class ServerErrorDetailHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if ((int)response.StatusCode >= 500)
        {
            throw new InvalidOperationException(
                $"{(int)response.StatusCode} from {request.Method} {request.RequestUri}: "
                + await response.Content.ReadAsStringAsync(cancellationToken));
        }

        return response;
    }
}

internal static class HttpExtensions
{
    public static HttpClient AuthClient(this PlanvexaFixture fixture, string subject, Guid? workspaceId = null)
    {
        var client = fixture.Factory.CreateDefaultClient(new ServerErrorDetailHandler());
        client.DefaultRequestHeaders.Add("X-Debug-Subject", subject);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{subject}@planvexa.test");
        if (workspaceId is { } id)
        {
            client.DefaultRequestHeaders.Add("X-Workspace", id.ToString());
        }

        return client;
    }

    public static Task<int> EmailCountForAsync(this PlanvexaFixture fixture, Guid userId)
    {
        var log = fixture.Factory.Services.GetRequiredService<Planvexa.Api.Notifications.SentEmailLog>();
        return Task.FromResult(log.ForRecipient(userId).Count);
    }

    public static Task<IReadOnlyList<Planvexa.Api.Notifications.SentEmail>> EmailsForAsync(this PlanvexaFixture fixture, Guid userId)
    {
        var log = fixture.Factory.Services.GetRequiredService<Planvexa.Api.Notifications.SentEmailLog>();
        return Task.FromResult(log.ForRecipient(userId));
    }

    public static Task<int> PushCountForAsync(this PlanvexaFixture fixture, Guid userId)
    {
        var log = fixture.Factory.Services.GetRequiredService<Planvexa.Api.Notifications.SentPushLog>();
        return Task.FromResult(log.ForRecipient(userId).Count);
    }

    public static async Task<(HttpResponseMessage Response, WorkspaceResponse Workspace)> RegisterOrgAsync(
        this HttpClient client, string slugOrWorkspaceName, string? name = null, string? workspaceName = null, string? slug = null)
    {
        var actualWorkspaceName = workspaceName ?? name ?? slugOrWorkspaceName;
        var actualSlug = slug ?? slugOrWorkspaceName;
        var response = await client.PostAsJsonAsync("/api/v1/workspaces", new { name = actualWorkspaceName, slug = actualSlug });

        WorkspaceResponse? workspace = null;
        if (response.IsSuccessStatusCode)
        {
            workspace = await response.Content.ReadFromJsonAsync<WorkspaceResponse>();
        }

        return (response, workspace!);
    }
}

