using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Issue #174, ask 4: <c>GetToggleAsync("X", false)</c> returning <c>false</c> is indistinguishable
/// from a server that says <c>false</c>, so an application pinned to its fallback by a sustained
/// fault looks exactly like one deliberately switched off. These pin that
/// <see cref="IFeatureToggleService.GetToggleResultAsync"/> reports the difference.
/// </summary>
public class ConfigurationProvenanceTests
{
    [Fact]
    public async Task Server_Value_Is_Reported_As_Server()
    {
        using var listener = StartListener(out var prefix, out var mode);
        var service = BuildToggleService(prefix);
        mode.Serve("{\"value\":\"True\",\"validTo\":\"" + Iso(TimeSpan.FromMinutes(10)) + "\"}");

        var result = await service.GetToggleResultAsync("AssistantPanel.Enabled", fallback: false);

        result.Value.Should().BeTrue();
        result.Source.Should().Be(ConfigurationSource.Server);
        result.Stale.Should().BeFalse();
    }

    [Fact]
    public async Task Second_Read_Within_The_Lifetime_Is_Reported_As_Cache()
    {
        using var listener = StartListener(out var prefix, out var mode);
        var service = BuildToggleService(prefix);
        mode.Serve("{\"value\":\"True\",\"validTo\":\"" + Iso(TimeSpan.FromMinutes(10)) + "\"}");

        await service.GetToggleResultAsync("AssistantPanel.Enabled", fallback: false);
        var result = await service.GetToggleResultAsync("AssistantPanel.Enabled", fallback: false);

        result.Source.Should().Be(ConfigurationSource.Cache);
        result.Stale.Should().BeFalse();
    }

    [Fact]
    public async Task Expired_Entry_Is_Reported_As_StaleCache()
    {
        using var listener = StartListener(out var prefix, out var mode);
        var service = BuildToggleService(prefix);
        // Already expired on arrival, so the next read takes the stale-while-revalidate path.
        mode.Serve("{\"value\":\"True\",\"validTo\":\"" + Iso(TimeSpan.FromSeconds(-1)) + "\"}");

        await service.GetToggleResultAsync("AssistantPanel.Enabled", fallback: false);
        var result = await service.GetToggleResultAsync("AssistantPanel.Enabled", fallback: false);

        result.Source.Should().Be(ConfigurationSource.StaleCache);
        result.Stale.Should().BeTrue();
        result.Value.Should().BeTrue("a stale server value is still the server's value");
    }

    [Fact]
    public async Task A_Fallback_Identical_To_The_Real_Value_Is_Still_Reported_As_Fallback()
    {
        // The case the plain read cannot express, and the whole reason this API exists: the caller's
        // fallback and the server's answer are both `true`, so the returned value is no evidence at
        // all of whether anything was reached.
        using var listener = StartListener(out var prefix, out var mode);
        var service = BuildToggleService(prefix);
        mode.Fail(HttpStatusCode.InternalServerError);

        var result = await service.GetToggleResultAsync("AssistantPanel.Enabled", fallback: true);

        result.Value.Should().BeTrue("the caller's fallback stands");
        result.Source.Should().Be(ConfigurationSource.Fallback,
            "nothing answered — a value equal to the fallback must not be reported as a server value");
        result.Stale.Should().BeTrue();
    }

    [Fact]
    public async Task A_Server_Response_Carrying_No_Value_Is_Reported_As_Fallback_But_Not_Stale()
    {
        // The server was reached and has nothing for this key. The caller is holding its fallback,
        // which is what provenance must say — but the answer is current, so it is not stale.
        using var listener = StartListener(out var prefix, out var mode);
        var service = BuildToggleService(prefix);
        mode.Serve("{\"value\":null,\"validTo\":\"" + Iso(TimeSpan.FromMinutes(10)) + "\"}");

        var result = await service.GetToggleResultAsync("Unknown.Toggle", fallback: true);

        result.Value.Should().BeTrue();
        result.Source.Should().Be(ConfigurationSource.Fallback);
        result.Stale.Should().BeFalse("the server answered; it simply has no value for the key");
    }

    [Fact]
    public async Task The_Plain_Read_Still_Returns_The_Same_Value()
    {
        // GetToggleResultAsync must not become a second, divergent read path.
        using var listener = StartListener(out var prefix, out var mode);
        var service = BuildToggleService(prefix);
        mode.Serve("{\"value\":\"True\",\"validTo\":\"" + Iso(TimeSpan.FromMinutes(10)) + "\"}");

        var plain = await service.GetToggleAsync("AssistantPanel.Enabled", fallback: false);
        var detailed = await service.GetToggleResultAsync("AssistantPanel.Enabled", fallback: false);

        plain.Should().Be(detailed.Value);
    }

    private static string Iso(TimeSpan offset) => DateTime.UtcNow.Add(offset).ToString("O");

    private static IFeatureToggleService BuildToggleService(string baseAddress)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetRemoteConfiguration(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                });
            })
            .Build();

        return host.Services.GetRequiredService<IFeatureToggleService>();
    }

    private static HttpListener StartListener(out string prefix, out RawResponseMode mode)
    {
        var port = GetFreePort();
        prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var state = new RawResponseMode();
        mode = state;

        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { return; }

                var (status, body) = state.Current;
                ctx.Response.StatusCode = (int)status;
                if (body != null)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(body);
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                }
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

    private sealed class RawResponseMode
    {
        private volatile Tuple<HttpStatusCode, string> _current = Tuple.Create(HttpStatusCode.OK, (string)null);

        public (HttpStatusCode Status, string Body) Current => (_current.Item1, _current.Item2);

        public void Serve(string json) => _current = Tuple.Create(HttpStatusCode.OK, json);

        public void Fail(HttpStatusCode status) => _current = Tuple.Create(status, (string)null);
    }
}
