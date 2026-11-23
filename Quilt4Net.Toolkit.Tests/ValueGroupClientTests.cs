using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quilt4Net.Toolkit.Features.ValueGroup;
using Xunit;

#pragma warning disable xUnit1051

namespace Quilt4Net.Toolkit.Tests;

public class ValueGroupClientTests
{
    private const string GroupId = "507f1f77bcf86cd799439011";

    [Fact]
    public async Task GetAsync_Returns_Bundle_On_200()
    {
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        _ = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var body = $$"""
                {
                    "groupId":"{{GroupId}}",
                    "groupName":"prod-eu",
                    "fetchedAtUtc":"2026-05-26T12:00:00Z",
                    "featureToggles":[{"key":"enabled","application":"App","environment":"Prod","instance":null,"value":"true","defaultValue":"false","valueType":"Boolean","lastUsed":"2026-05-26T12:00:00Z","ttl":null}],
                    "applicationInsightsConfigurations":[{"id":"a","name":"prod","tenantId":"t","workspaceId":"w","clientId":"c","clientSecret":"s"}]
                }
                """;
            var buf = System.Text.Encoding.UTF8.GetBytes(body);
            await ctx.Response.OutputStream.WriteAsync(buf);
            ctx.Response.Close();
        });

        try
        {
            var host = BuildHost(prefix);
            var client = host.Services.GetRequiredService<IValueGroupClient>();

            var bundle = await client.GetAsync();

            bundle.GroupId.Should().Be(GroupId);
            bundle.GroupName.Should().Be("prod-eu");
            bundle.FeatureToggles.Should().HaveCount(1);
            bundle.ApplicationInsightsConfigurations.Should().HaveCount(1);
            bundle.ApplicationInsightsConfigurations[0].ClientSecret.Should().Be("s");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetAsync_Throws_ValueGroupAuthorizationException_On_401()
    {
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        _ = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            ctx.Response.StatusCode = 401;
            ctx.Response.Close();
        });

        try
        {
            var host = BuildHost(prefix);
            var client = host.Services.GetRequiredService<IValueGroupClient>();

            var act = () => client.GetAsync();
            await act.Should().ThrowAsync<ValueGroupAuthorizationException>()
                .WithMessage("*401*");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetAsync_Throws_ValueGroupAuthorizationException_On_403()
    {
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        _ = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            ctx.Response.StatusCode = 403;
            ctx.Response.Close();
        });

        try
        {
            var host = BuildHost(prefix);
            var client = host.Services.GetRequiredService<IValueGroupClient>();

            var act = () => client.GetAsync();
            await act.Should().ThrowAsync<ValueGroupAuthorizationException>()
                .WithMessage("*403*");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetAsync_Caches_Within_Ttl()
    {
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var hits = 0;
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { return; }
                Interlocked.Increment(ref hits);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                var body = $$"""{"groupId":"{{GroupId}}","groupName":"prod-eu","featureToggles":[],"applicationInsightsConfigurations":[]}""";
                var buf = System.Text.Encoding.UTF8.GetBytes(body);
                await ctx.Response.OutputStream.WriteAsync(buf);
                ctx.Response.Close();
            }
        });

        try
        {
            var host = BuildHost(prefix, ttl: TimeSpan.FromMinutes(10));
            var client = host.Services.GetRequiredService<IValueGroupClient>();

            await client.GetAsync();
            await client.GetAsync();
            await client.GetAsync();

            hits.Should().Be(1, "subsequent calls within Ttl serve from cache");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetAsync_Returns_Stale_On_Server_Error_When_Cache_Exists()
    {
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var requestCount = 0;
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { return; }
                var n = Interlocked.Increment(ref requestCount);
                if (n == 1)
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    var body = $$"""{"groupId":"{{GroupId}}","groupName":"prod-eu","featureToggles":[],"applicationInsightsConfigurations":[]}""";
                    var buf = System.Text.Encoding.UTF8.GetBytes(body);
                    await ctx.Response.OutputStream.WriteAsync(buf);
                }
                else
                {
                    ctx.Response.StatusCode = 500;
                }
                ctx.Response.Close();
            }
        });

        try
        {
            // Very short Ttl so the second call goes to the server and gets the 500.
            var host = BuildHost(prefix, ttl: TimeSpan.FromMilliseconds(1));
            var client = host.Services.GetRequiredService<IValueGroupClient>();

            var first = await client.GetAsync();
            first.GroupName.Should().Be("prod-eu");

            await Task.Delay(50);
            var second = await client.GetAsync();
            second.GroupName.Should().Be("prod-eu", "transient 500 must serve the stale-cache, not throw");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Registration_Validates_Required_Options()
    {
        // Missing ApiKey
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetValueGroupClient(null, o =>
                {
                    o.Quilt4NetAddress = "http://127.0.0.1:9999/";
                    o.ApiKey = null;
                });
            })
            .Build();

        var act = () => host.Services.GetRequiredService<IValueGroupClient>();
        act.Should().Throw<InvalidOperationException>().WithMessage("*ApiKey*");
    }

    private static IHost BuildHost(string address, TimeSpan? ttl = null)
        => Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetValueGroupClient(null, o =>
                {
                    o.Quilt4NetAddress = address;
                    o.ApiKey = "test-key";
                    o.Ttl = ttl;
                    o.HttpTimeout = TimeSpan.FromSeconds(2);
                });
            })
            .Build();

    private static int GetFreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
