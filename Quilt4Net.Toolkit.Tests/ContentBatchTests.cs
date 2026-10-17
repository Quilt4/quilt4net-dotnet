using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Keys the cache does not hold are sent to the server together, not one request each.
/// </summary>
/// <remarks>
/// A page of new keys used to send one request per key, all at once, and each made the server do its
/// own database reads and writes. One client sent 191 of them during a single slowdown.
/// </remarks>
public class ContentBatchTests
{
    private const string App = "App1";

    [Fact]
    public async Task Uncached_keys_asked_for_together_cost_one_call()
    {
        using var listener = StartListener(out var prefix, out var state);
        var content = Build(prefix);

        var values = await content.GetManyContentAsync([Req("k1"), Req("k2"), Req("k3")], Guid.Empty, ContentFormat.String, App);

        values["k1"].Should().Be("server:k1");
        values["k2"].Should().Be("server:k2");
        values["k3"].Should().Be("server:k3", "each key gets its own answer, matched by position");
        state.BatchCalls.Should().Be(1);
        state.SingleKeyCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_key_alone_in_its_window_is_sent_on_its_own()
    {
        using var listener = StartListener(out var prefix, out var state);
        var content = Build(prefix);

        var result = await content.GetContentAsync("k1", "def", Guid.Empty, ContentFormat.String, App);

        result.Value.Should().Be("server:k1");
        state.BatchCalls.Should().Be(0, "a batch of one gains nothing, and the single-key call works on every server version");
        state.SingleKeyCalls.Should().Be(1);
    }

    [Fact]
    public async Task A_server_without_batching_is_asked_key_by_key_and_only_tried_once()
    {
        using var listener = StartListener(out var prefix, out var state, batchSupported: false);
        var content = Build(prefix);

        var first = await content.GetManyContentAsync([Req("k1"), Req("k2")], Guid.Empty, ContentFormat.String, App);
        var second = await content.GetManyContentAsync([Req("k3"), Req("k4")], Guid.Empty, ContentFormat.String, App);

        first["k1"].Should().Be("server:k1");
        second["k4"].Should().Be("server:k4");
        state.BatchCalls.Should().Be(1, "after one refusal the client stops asking, or every page would waste a call");
        state.SingleKeyCalls.Should().Be(4);
    }

    [Fact]
    public async Task A_failed_batch_falls_back_per_key_like_a_failed_single_call()
    {
        using var listener = StartListener(out var prefix, out _, batchStatus: 503);
        var content = Build(prefix);

        var values = await content.GetManyContentAsync([Req("k1", "d1"), Req("k2", "d2")], Guid.Empty, ContentFormat.String, App);

        values["k1"].Should().Be("d1");
        values["k2"].Should().Be("d2");
    }

    [Fact]
    public async Task A_zero_window_turns_batching_off()
    {
        using var listener = StartListener(out var prefix, out var state);
        var content = Build(prefix, window: TimeSpan.Zero);

        await content.GetManyContentAsync([Req("k1"), Req("k2")], Guid.Empty, ContentFormat.String, App);

        state.BatchCalls.Should().Be(0);
        state.SingleKeyCalls.Should().Be(2);
    }

    private static ContentRequest Req(string key, string defaultValue = "def") => new() { Key = key, DefaultValue = defaultValue };

    private static IContentService Build(string baseAddress, TimeSpan? window = null)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    o.WarmUpEnabled = false;
                    o.HttpTimeout = TimeSpan.FromSeconds(10);
                    o.MaterializationWait = TimeSpan.Zero; // wait for the real answer, so the mapping can be checked
                    if (window.HasValue) o.BatchWindow = window.Value;
                });
            })
            .Build();

        return host.Services.GetRequiredService<IContentService>();
    }

    private sealed class ListenerState
    {
        public int BatchCalls;
        public int SingleKeyCalls;
    }

    private static HttpListener StartListener(out string prefix, out ListenerState state, bool batchSupported = true, int batchStatus = 200)
    {
        var port = GetFreePort();
        prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var s = new ListenerState();
        state = s;
        var sync = new object();

        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { return; }

                var segments = ctx.Request.Url!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var validTo = DateTime.UtcNow.AddHours(1).ToString("o");
                string body;

                if (segments.Length >= 3 && segments[2].Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = 200;
                    body = $$"""{"items":[],"validTo":"{{validTo}}"}""";
                }
                else if (ctx.Request.HttpMethod == "POST" && segments.Length >= 3 && segments[2].Equals("batch", StringComparison.OrdinalIgnoreCase))
                {
                    lock (sync) s.BatchCalls++;
                    if (!batchSupported)
                    {
                        ctx.Response.StatusCode = 404;
                        body = "";
                    }
                    else if (batchStatus != 200)
                    {
                        ctx.Response.StatusCode = batchStatus;
                        body = "";
                    }
                    else
                    {
                        using var reader = new StreamReader(ctx.Request.InputStream);
                        using var doc = JsonDocument.Parse(await reader.ReadToEndAsync());
                        var keys = doc.RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("key").GetString());
                        var items = string.Join(",", keys.Select(k => $$"""{"value":"server:{{k}}","validTo":"{{validTo}}"}"""));
                        ctx.Response.StatusCode = 200;
                        body = $$"""{"items":[{{items}}]}""";
                    }
                }
                else
                {
                    lock (sync) s.SingleKeyCalls++;
                    var complexKey = WebUtility.UrlDecode(segments[^1]);
                    using var doc = JsonDocument.Parse(Convert.FromBase64String(complexKey));
                    var key = doc.RootElement.GetProperty("Key").GetString();
                    ctx.Response.StatusCode = 200;
                    body = $$"""{"value":"server:{{key}}","validTo":"{{validTo}}"}""";
                }

                ctx.Response.ContentType = "application/json";
                var buf = System.Text.Encoding.UTF8.GetBytes(body);
                try
                {
                    await ctx.Response.OutputStream.WriteAsync(buf);
                    ctx.Response.Close();
                }
                catch
                {
                    // The caller may have gone. Not a failure.
                }
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
