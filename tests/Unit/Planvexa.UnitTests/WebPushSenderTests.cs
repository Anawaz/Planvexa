namespace Planvexa.UnitTests.Platform;

using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Planvexa.Api.Notifications;
using Planvexa.SharedContracts.Mobile;
using Shouldly;
using Xunit;

/// <summary>
/// WebPushSender orchestration (HTTP request shape, 404/410 expiry handling) with the HttpClient mocked --
/// no real delivery to a push endpoint. RFC 8291/8292 crypto correctness itself is covered by
/// <see cref="WebPushCryptoTests"/>.
/// </summary>
public sealed class WebPushSenderTests
{
    private static readonly IConfiguration EmptyConfig = new ConfigurationBuilder().Build();

    [Fact]
    public async Task SendAsync_posts_an_encrypted_vapid_signed_request_per_subscription()
    {
        var deviceId = Guid.CreateVersion7();
        var directory = new FakePushDeviceDirectory([new PushSubscription(deviceId, "https://push.example.com/xyz", ValidP256dh(), ValidAuth())]);
        var handler = new CapturingHandler(HttpStatusCode.Created);
        var sender = new WebPushSender(new FakeHttpClientFactory(handler), new VapidKeyProvider(), directory, EmptyConfig, NullLogger<WebPushSender>.Instance);

        await sender.SendAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), "title", "body");

        handler.Requests.Count.ShouldBe(1);
        var request = handler.Requests[0];
        request.RequestUri.ShouldBe(new Uri("https://push.example.com/xyz"));
        request.Content!.Headers.ContentType!.MediaType.ShouldBe("application/octet-stream");
        request.Content.Headers.ContentEncoding.ShouldContain("aes128gcm");
        request.Headers.GetValues("Authorization").Single().ShouldStartWith("vapid t=");
        request.Headers.GetValues("Crypto-Key").Single().ShouldStartWith("p256ecdsa=");
        directory.ExpiredDeviceIds.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task SendAsync_marks_the_subscription_expired_on_404_or_410(HttpStatusCode status)
    {
        var deviceId = Guid.CreateVersion7();
        var directory = new FakePushDeviceDirectory([new PushSubscription(deviceId, "https://push.example.com/xyz", ValidP256dh(), ValidAuth())]);
        var sender = new WebPushSender(new FakeHttpClientFactory(new CapturingHandler(status)), new VapidKeyProvider(), directory, EmptyConfig, NullLogger<WebPushSender>.Instance);

        await sender.SendAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), "title", "body");

        directory.ExpiredDeviceIds.ShouldBe([deviceId]);
    }

    [Fact]
    public async Task SendAsync_does_nothing_when_the_user_has_no_web_push_subscription()
    {
        var directory = new FakePushDeviceDirectory([]);
        var handler = new CapturingHandler(HttpStatusCode.Created);
        var sender = new WebPushSender(new FakeHttpClientFactory(handler), new VapidKeyProvider(), directory, EmptyConfig, NullLogger<WebPushSender>.Instance);

        await sender.SendAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), "title", "body");

        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task SendAsync_does_not_throw_when_one_of_several_devices_fails()
    {
        var okDevice = Guid.CreateVersion7();
        var directory = new FakePushDeviceDirectory([
            new PushSubscription(Guid.CreateVersion7(), "https://push.example.com/bad", "not-valid-base64url-p256dh!!", ValidAuth()),
            new PushSubscription(okDevice, "https://push.example.com/good", ValidP256dh(), ValidAuth()),
        ]);
        var handler = new CapturingHandler(HttpStatusCode.Created);
        var sender = new WebPushSender(new FakeHttpClientFactory(handler), new VapidKeyProvider(), directory, EmptyConfig, NullLogger<WebPushSender>.Instance);

        await Should.NotThrowAsync(() => sender.SendAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), "title", "body"));

        handler.Requests.Count.ShouldBe(1);
        handler.Requests[0].RequestUri.ShouldBe(new Uri("https://push.example.com/good"));
    }

    private static string ValidP256dh()
    {
        using var key = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(false);
        var point = new byte[65];
        point[0] = 0x04;
        p.Q.X!.CopyTo(point, 1);
        p.Q.Y!.CopyTo(point, 33);
        return Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(point);
    }

    private static string ValidAuth()
        => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

    private sealed class FakePushDeviceDirectory(IReadOnlyList<PushSubscription> subscriptions) : IPushDeviceDirectory
    {
        public List<Guid> ExpiredDeviceIds { get; } = [];

        public Task<bool> HasActiveDeviceAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(subscriptions.Count > 0);

        public Task<IReadOnlyList<PushSubscription>> ListSubscriptionsAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(subscriptions);

        public Task MarkPushSubscriptionExpiredAsync(Guid deviceId, CancellationToken cancellationToken = default)
        {
            ExpiredDeviceIds.Add(deviceId);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
