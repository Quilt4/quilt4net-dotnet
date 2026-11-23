using System.Collections.Concurrent;
using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quilt4Net.Toolkit.Features.Content;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Issue #163 ask 1: content could not ask for a longer lifetime, where remote configuration always
/// could. The lifetime is what sets the periodic re-warm interval — the client re-warms at 80% of
/// whatever the server reports — so requesting 24 hours turns a bulk call every 8 minutes into one
/// every 19.2 hours with nothing else to change.
/// </summary>
public class ContentCacheDurationTests
{
    [Fact]
    public async Task The_requested_lifetime_is_sent_on_the_bulk_warm_up()
    {
        using var listener = StartListener(out var prefix, out var queries);
        var remote = BuildRemoteService(prefix, o => o.CacheDuration = TimeSpan.FromHours(24));

        await remote.WarmCacheAsync(Guid.Empty, application: "App");

        queries.Should().ContainSingle();
        queries.Single().Should().Contain("ttl=", "the bulk endpoint takes no body, so the lifetime has to ride as a query parameter");
        queries.Single().Should().Contain("1.00%3A00%3A00", "24 hours, round-tripped in the invariant TimeSpan format");
    }

    [Fact]
    public async Task Nothing_is_sent_when_no_lifetime_is_requested()
    {
        // The default must leave the request byte-identical to before the option existed, so an
        // upgrade changes nothing until somebody opts in.
        using var listener = StartListener(out var prefix, out var queries);
        var remote = BuildRemoteService(prefix, _ => { });

        await remote.WarmCacheAsync(Guid.Empty, application: "App");

        queries.Should().ContainSingle();
        queries.Single().Should().BeEmpty();
    }

    [Fact]
    public async Task A_zero_or_negative_lifetime_is_not_sent()
    {
        // A lifetime already in the past would make every response instantly stale. Treated as "no
        // opinion" on the client too, so a misconfiguration cannot reach the wire.
        using var listener = StartListener(out var prefix, out var queries);
        var remote = BuildRemoteService(prefix, o => o.CacheDuration = TimeSpan.Zero);

        await remote.WarmCacheAsync(Guid.Empty, application: "App");

        queries.Single().Should().BeEmpty();
    }

    [Fact]
    public void The_option_is_null_by_default()
    {
        new ContentOptions().CacheDuration.Should().BeNull("the server's own lifetime applies until a host opts in");
    }

    private static IRemoteContentCallService BuildRemoteService(string baseAddress, Action<ContentOptions> configure)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    o.WarmUpEnabled = false;
                    configure(o);
                });
            })
            .Build();

        return host.Services.GetRequiredService<IRemoteContentCallService>();
    }

    private static HttpListener StartListener(out string prefix, out ConcurrentBag<string> queries)
    {
        var port = GetFreePort();
        prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var seen = new ConcurrentBag<string>();
        queries = seen;

        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { return; }

                seen.Add(ctx.Request.Url?.Query ?? "");

                var json = $"{{\"items\":[],\"validTo\":\"{DateTime.UtcNow.AddMinutes(10):O}\"}}";
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(bytes);
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
