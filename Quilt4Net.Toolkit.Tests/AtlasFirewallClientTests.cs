using System.Net;
using System.Net.Sockets;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quilt4Net.Toolkit;
using Quilt4Net.Toolkit.Features.Atlas;
using Quilt4Net.Toolkit.Features.ValueGroup;
using Xunit;

#pragma warning disable xUnit1051

namespace Quilt4Net.Toolkit.Tests;

public class AtlasFirewallClientTests
{
    private const string GroupId = "507f1f77bcf86cd799439011";

    [Fact]
    public async Task OpenAsync_sends_key_group_ip_and_parses_outcome()
    {
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        string capturedKey = null, capturedPath = null, capturedBody = null;
        _ = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            capturedKey = ctx.Request.Headers["X-API-KEY"];
            capturedPath = ctx.Request.Url!.AbsolutePath;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                capturedBody = await reader.ReadToEndAsync();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var buf = Encoding.UTF8.GetBytes("""{"outcome":"Opened","ip":"1.2.3.4","openedUtc":"2026-06-04T10:00:00Z"}""");
            await ctx.Response.OutputStream.WriteAsync(buf);
            ctx.Response.Close();
        });

        try
        {
            var client = BuildFactory(prefix).Create(Entry(canManage: true));

            var result = await client.OpenAsync("1.2.3.4", "my laptop");

            result.Outcome.Should().Be("Opened");
            result.Ip.Should().Be("1.2.3.4");
            capturedKey.Should().Be("fw-key");
            capturedPath.Should().Be("/Api/AtlasFirewall/open");
            capturedBody.Should().Contain(GroupId).And.Contain("1.2.3.4");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task OpenAsync_on_usage_only_key_throws_without_calling_server()
    {
        var client = BuildFactory("http://127.0.0.1:1/").Create(Entry(canManage: false));

        var act = () => client.OpenAsync("1.2.3.4");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*usage-only*");
    }

    [Fact]
    public async Task ReportUsedAsync_works_with_a_usage_only_key()
    {
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        string capturedPath = null;
        _ = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            capturedPath = ctx.Request.Url!.AbsolutePath;
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var buf = Encoding.UTF8.GetBytes("""{"outcome":"Recorded"}""");
            await ctx.Response.OutputStream.WriteAsync(buf);
            ctx.Response.Close();
        });

        try
        {
            var client = BuildFactory(prefix).Create(Entry(canManage: false));

            var result = await client.ReportUsedAsync("1.2.3.4");

            result.Outcome.Should().Be("Recorded");
            capturedPath.Should().Be("/Api/AtlasFirewall/used");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Firewall_call_throws_AtlasFirewallAuthorizationException_on_403()
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
            var client = BuildFactory(prefix).Create(Entry(canManage: true));

            var act = () => client.OpenAsync("1.2.3.4");

            await act.Should().ThrowAsync<AtlasFirewallAuthorizationException>().WithMessage("*403*");
        }
        finally
        {
            listener.Stop();
        }
    }

    private static AtlasFirewallProxyKeyEntry Entry(bool canManage) =>
        new() { Name = "fw", ApiKey = "fw-key", GroupId = GroupId, CanManage = canManage };

    private static IAtlasFirewallClientFactory BuildFactory(string address)
        => Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddQuilt4NetAtlasFirewallClient(null, o =>
            {
                o.Quilt4NetAddress = address;
                o.HttpTimeout = TimeSpan.FromSeconds(2);
            }))
            .Build()
            .Services.GetRequiredService<IAtlasFirewallClientFactory>();

    private static int GetFreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
