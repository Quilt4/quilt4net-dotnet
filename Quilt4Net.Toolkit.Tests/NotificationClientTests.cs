using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Features.Notification;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Wire-shape tests for the notification client: the URL it posts to, what it fills in, and how it
/// reports failure.
/// </summary>
public class NotificationClientTests
{
    private const string StoredNotification = """{"id":"6718f0c2a1b2c3d4e5f60718","category":"alarm","message":"Worker down","application":"App1","environment":"Production","instance":"node-1","createdUtc":"2026-10-24T09:00:00Z"}""";

    [Fact]
    public async Task SendAsync_posts_category_and_message()
    {
        var handler = new RecordingHandler(_ => Json(StoredNotification));
        var sut = CreateSut(handler);

        var result = await sut.SendAsync("alarm", "Worker down", TestContext.Current.CancellationToken);

        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/Api/Notification");
        var body = JsonDocument.Parse(handler.LastBody).RootElement;
        body.GetProperty("category").GetString().Should().Be("alarm");
        body.GetProperty("message").GetString().Should().Be("Worker down");
        result.Id.Should().Be("6718f0c2a1b2c3d4e5f60718");
    }

    [Fact]
    public async Task SendAsync_fills_in_the_sender_from_options_and_machine()
    {
        var handler = new RecordingHandler(_ => Json(StoredNotification));
        var sut = CreateSut(handler, new NotificationOptions { Application = "App1", Environment = "Production" });

        await sut.SendAsync("alarm", "Worker down", TestContext.Current.CancellationToken);

        var body = JsonDocument.Parse(handler.LastBody).RootElement;
        body.GetProperty("application").GetString().Should().Be("App1");
        body.GetProperty("environment").GetString().Should().Be("Production");
        body.GetProperty("instance").GetString().Should().Be(Environment.MachineName);
    }

    [Fact]
    public async Task SendAsync_keeps_what_the_caller_set()
    {
        var handler = new RecordingHandler(_ => Json(StoredNotification));
        var sut = CreateSut(handler, new NotificationOptions { Application = "App1" });

        await sut.SendAsync(new SendNotificationRequest { Category = "info", Message = "x", Application = "Other", Instance = "node-7" }, TestContext.Current.CancellationToken);

        var body = JsonDocument.Parse(handler.LastBody).RootElement;
        body.GetProperty("application").GetString().Should().Be("Other");
        body.GetProperty("instance").GetString().Should().Be("node-7");
    }

    [Fact]
    public async Task A_refusal_throws_with_the_status_and_the_server_message()
    {
        var sut = CreateSut(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("Missing required scope 'notification:write'") }));

        var act = () => sut.SendAsync("alarm", "x");

        var thrown = await act.Should().ThrowAsync<NotificationClientException>();
        thrown.Which.StatusCode.Should().Be(403);
        thrown.Which.Message.Should().Contain("notification:write");
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    private static NotificationClient CreateSut(HttpMessageHandler handler, NotificationOptions options = null) =>
        new(new StubHttpClientFactory(handler), Options.Create(options ?? new NotificationOptions()));

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage LastRequest { get; private set; }
        public string LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://example.com/") };
    }
}
