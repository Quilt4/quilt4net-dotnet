using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

// Matches the surrounding test files (FeatureToggleTests etc.) — these HttpListener-backed
// tests are short-lived and don't need TestContext-driven cancellation.
#pragma warning disable xUnit1051

namespace Quilt4Net.Toolkit.Tests;

public class RemoteApplicationInsightsConfigurationProviderTests
{
    [Fact]
    public async Task GetAllAsync_Returns_Configurations_When_Server_Returns_200()
    {
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var listenerTask = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var body = """
                [
                    {"id":"a","name":"Prod","tenantId":"t1","workspaceId":"w1","clientId":"c1","clientSecret":"s1"},
                    {"id":"b","name":"Test","tenantId":"t2","workspaceId":"w2","clientId":"c2","clientSecret":"s2"}
                ]
                """;
            var buf = System.Text.Encoding.UTF8.GetBytes(body);
            await ctx.Response.OutputStream.WriteAsync(buf);
            ctx.Response.Close();
        });

        try
        {
            var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddQuilt4NetApplicationInsightsClientRemote(null, o =>
                    {
                        o.Quilt4NetAddress = prefix;
                        o.ApiKey = "test-key";
                    });
                })
                .Build();

            var provider = host.Services.GetRequiredService<IApplicationInsightsConfigurationProvider>();
            var configs = await provider.GetAllAsync();

            configs.Should().HaveCount(2);
            configs[0].Id.Should().Be("a");
            configs[0].Name.Should().Be("Prod");
            configs[0].TenantId.Should().Be("t1");
            configs[1].ClientSecret.Should().Be("s2");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetAllAsync_Returns_Empty_When_Server_Returns_401()
    {
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var listenerTask = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            ctx.Response.StatusCode = 401;
            ctx.Response.Close();
        });

        try
        {
            var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddQuilt4NetApplicationInsightsClientRemote(null, o =>
                    {
                        o.Quilt4NetAddress = prefix;
                        o.ApiKey = "test-key";
                    });
                })
                .Build();

            var provider = host.Services.GetRequiredService<IApplicationInsightsConfigurationProvider>();
            var configs = await provider.GetAllAsync();

            configs.Should().BeEmpty("missing scope or invalid key must not throw — graceful empty list");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetAllAsync_Returns_Empty_When_Server_Unreachable()
    {
        var port = GetFreePort();
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetApplicationInsightsClientRemote(null, o =>
                {
                    o.Quilt4NetAddress = $"http://127.0.0.1:{port}/";
                    o.ApiKey = "test-key";
                    o.HttpTimeout = TimeSpan.FromMilliseconds(500);
                });
            })
            .Build();

        var provider = host.Services.GetRequiredService<IApplicationInsightsConfigurationProvider>();
        var configs = await provider.GetAllAsync();

        configs.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_Caches_Within_Ttl()
    {
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var hits = 0;
        var listenerTask = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { return; }
                Interlocked.Increment(ref hits);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                var body = """[{"id":"a","name":"Prod","tenantId":"t","workspaceId":"w","clientId":"c","clientSecret":"s"}]""";
                var buf = System.Text.Encoding.UTF8.GetBytes(body);
                await ctx.Response.OutputStream.WriteAsync(buf);
                ctx.Response.Close();
            }
        });

        try
        {
            var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddQuilt4NetApplicationInsightsClientRemote(null, o =>
                    {
                        o.Quilt4NetAddress = prefix;
                        o.ApiKey = "test-key";
                        o.Ttl = TimeSpan.FromMinutes(10);
                    });
                })
                .Build();

            var provider = host.Services.GetRequiredService<IApplicationInsightsConfigurationProvider>();

            await provider.GetAllAsync();
            await provider.GetAllAsync();
            await provider.GetAllAsync();

            hits.Should().Be(1, "subsequent calls within Ttl serve from cache");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Registration_Resolves_Required_Services()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetApplicationInsightsClientRemote(null, o =>
                {
                    o.Quilt4NetAddress = "http://127.0.0.1:9999/";
                    o.ApiKey = "test-key";
                });
            })
            .Build();

        host.Services.GetService<IApplicationInsightsConfigurationProvider>().Should().NotBeNull();
        host.Services.GetService<IApplicationInsightsService>().Should().NotBeNull();
        host.Services.GetService<IVersionMatrixService>().Should().NotBeNull();
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
