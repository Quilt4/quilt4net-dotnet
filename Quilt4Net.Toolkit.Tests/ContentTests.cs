using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class ContentTests
{
    [Fact]
    public async Task GetContentAsync_With_Explicit_Application_Sends_That_Value()
    {
        using var listener = StartListener(out var prefix, out var captured);

        var service = BuildContentService(prefix);

        await service.GetContentAsync("my-key", "default", Guid.NewGuid(), ContentFormat.String, application: "Yee");

        captured.Should().ContainSingle();
        captured[0].Should().Be("Yee");
    }

    [Fact]
    public async Task GetContentAsync_With_Empty_Application_Sends_Empty_For_Shared()
    {
        using var listener = StartListener(out var prefix, out var captured);

        var service = BuildContentService(prefix);

        await service.GetContentAsync("my-key", "default", Guid.NewGuid(), ContentFormat.String, application: "");

        captured.Should().ContainSingle();
        captured[0].Should().Be("",
            "empty string is the explicit 'shared' sentinel and must be forwarded as-is — the toolkit must not substitute a default");
    }

    [Fact]
    public async Task GetContentAsync_With_Null_Application_Resolves_To_Non_Null_Value()
    {
        using var listener = StartListener(out var prefix, out var captured);

        var service = BuildContentService(prefix);

        await service.GetContentAsync("my-key", "default", Guid.NewGuid(), ContentFormat.String, application: null);

        captured.Should().ContainSingle();
        captured[0].Should().NotBeNullOrEmpty(
            "null is 'default — toolkit resolves the current application name'. It must never be sent as null/empty; that would mean shared.");
    }

    [Fact]
    public async Task GetContentAsync_Same_Key_Different_Application_Are_Separate_Cache_Entries()
    {
        using var listener = StartListener(out var prefix, out var captured);

        var service = BuildContentService(prefix);
        var languageKey = Guid.NewGuid();

        await service.GetContentAsync("my-key", "default", languageKey, ContentFormat.String, application: "App1");
        await service.GetContentAsync("my-key", "default", languageKey, ContentFormat.String, application: "App2");

        captured.Should().BeEquivalentTo(["App1", "App2"],
            "same key + language + different application must be separate cache entries — otherwise the second call silently returns the first call's value");
    }

    private static IContentService BuildContentService(string baseAddress)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    // Wait for the server's answer: these tests read what the round trip did, and the
                    // batch window plus the server delay can outlast the default wait.
                    o.MaterializationWait = TimeSpan.Zero;
                });
            })
            .Build();

        return host.Services.GetRequiredService<IContentService>();
    }

    private static HttpListener StartListener(out string prefix, out List<string> capturedApplications)
    {
        var port = GetFreePort();
        prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var captured = new List<string>();
        var captureLock = new object();
        capturedApplications = captured;

        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { return; }

                // A server from before batching answers the batch call 404, and the client then asks key
                // by key - what these tests were written against.
                if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url!.AbsolutePath.EndsWith("/batch", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    continue;
                }

                // URL is /Api/Content/{base64(GetContentRequest)} — decode to capture the Application field.
                var segments = ctx.Request.Url!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 3 && string.Equals(segments[0], "Api", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(segments[1], "Content", StringComparison.OrdinalIgnoreCase))
                {
                    var encoded = WebUtility.UrlDecode(segments[2]);
                    var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Application", out var appProp))
                    {
                        lock (captureLock)
                        {
                            captured.Add(appProp.ValueKind == System.Text.Json.JsonValueKind.Null
                                ? null
                                : appProp.GetString());
                        }
                    }
                }

                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                var body = $$"""{"value":"server-value","validTo":"{{DateTime.UtcNow.AddHours(1):o}}"}""";
                var buf = System.Text.Encoding.UTF8.GetBytes(body);
                await ctx.Response.OutputStream.WriteAsync(buf);
                ctx.Response.Close();
            }
        });

        return listener;
    }

    private static int GetFreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
