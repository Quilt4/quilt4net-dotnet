using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class StaleWhileRevalidateTests
{
    [Fact]
    public void Default_is_enabled_on_both_options()
    {
        new ContentOptions().StaleWhileRevalidate.Should().BeTrue();
        new RemoteConfigurationOptions().StaleWhileRevalidate.Should().BeTrue();
    }

    [Fact]
    public async Task Disabled_refreshes_synchronously_on_expiry_so_caller_sees_the_new_value()
    {
        // Server returns an already-expired TTL each time + a per-call incrementing value.
        using var listener = StartCountingListener(out var prefix, out var hits);
        var service = BuildContentService(prefix, staleWhileRevalidate: false);
        var lang = Guid.NewGuid();

        var (first, _) = await service.GetContentAsync("k", "default", lang, ContentFormat.String, "App");
        var (second, _) = await service.GetContentAsync("k", "default", lang, ContentFormat.String, "App");

        // With SWR off, the expired entry is refreshed synchronously → the 2nd call hits the server
        // again and returns the fresh value, not the stale one.
        first.Should().Be("value-1");
        second.Should().Be("value-2", "SWR disabled → expired cache is refreshed synchronously before returning");
        hits().Should().Be(2);
    }

    [Fact]
    public async Task Enabled_returns_stale_immediately_on_expiry()
    {
        using var listener = StartCountingListener(out var prefix, out var hits);
        var service = BuildContentService(prefix, staleWhileRevalidate: true);
        var lang = Guid.NewGuid();

        var (first, _) = await service.GetContentAsync("k", "default", lang, ContentFormat.String, "App");
        var (second, _) = await service.GetContentAsync("k", "default", lang, ContentFormat.String, "App");

        // With SWR on, the 2nd call returns the cached (stale) value immediately; the refresh happens
        // in the background. The synchronously-observed value is the first one.
        first.Should().Be("value-1");
        second.Should().Be("value-1", "SWR enabled → stale value is returned immediately on expiry");
    }

    private static IContentService BuildContentService(string baseAddress, bool staleWhileRevalidate)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    o.StaleWhileRevalidate = staleWhileRevalidate;
                });
            })
            .Build();

        return host.Services.GetRequiredService<IContentService>();
    }

    // Returns an already-expired validTo and an incrementing value ("value-1", "value-2", …) so a
    // refresh is always needed and each server hit is distinguishable. hits() reports the count.
    private static HttpListener StartCountingListener(out string prefix, out Func<int> hits)
    {
        var port = GetFreePort();
        prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var count = 0;
        var gate = new object();
        hits = () => { lock (gate) return count; };

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

                int n;
                lock (gate) n = ++count;

                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                // validTo in the past → the entry is immediately expired, forcing the SWR decision.
                var body = $$"""{"value":"value-{{n}}","validTo":"{{DateTime.UtcNow.AddSeconds(-1):o}}"}""";
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
