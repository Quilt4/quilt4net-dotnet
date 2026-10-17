using System.Net;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Bulk reads (#183) and the bounded materialization wait (#182). The two were reported separately
/// and are tested together because they fix the same user-visible thing from opposite ends: a view
/// that declares many keys taking seconds to open the first time after a release.
/// </summary>
public class ContentBulkGetTests
{
    private const string App = "App1";

    [Fact]
    public async Task GetMany_serves_warm_keys_without_a_call_per_key()
    {
        using var listener = StartListener(out var prefix, out var state, bulkItems: [("k1", "v1"), ("k2", "v2"), ("k3", "v3")]);
        var (remote, content) = Build(prefix);

        await remote.WarmCacheAsync(Guid.Empty, App);
        state.SingleKeyCalls = 0;

        var values = await content.GetManyContentAsync(
            [Req("k1"), Req("k2"), Req("k3")], Guid.Empty, ContentFormat.String, App);

        values["k1"].Should().Be("v1");
        values["k2"].Should().Be("v2");
        values["k3"].Should().Be("v3");
        state.SingleKeyCalls.Should().Be(0, "the whole point is that a warm set costs no round trips at all");
    }

    [Fact]
    public async Task GetMany_warms_a_cold_language_once_instead_of_fetching_each_key()
    {
        // The case the warm-up structurally cannot cover: a language the user selected at runtime
        // that nobody listed in WarmUpLanguages. Before, a 40-key view meant 40 calls.
        var swedish = Guid.NewGuid();
        using var listener = StartListener(out var prefix, out var state, bulkItems: [("k1", "v1"), ("k2", "v2")]);
        var (_, content) = Build(prefix);

        var values = await content.GetManyContentAsync([Req("k1"), Req("k2")], swedish, ContentFormat.String, App);

        values["k1"].Should().Be("v1");
        values["k2"].Should().Be("v2");
        state.BulkCalls.Should().Be(1, "one bulk warm for the language, not one call per key");
        state.SingleKeyCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetMany_returns_the_callers_default_for_a_key_the_server_does_not_have()
    {
        using var listener = StartListener(out var prefix, out _, bulkItems: [], singleKeyStatus: 404);
        var (_, content) = Build(prefix);

        var values = await content.GetManyContentAsync([Req("missing", "my-default")], Guid.Empty, ContentFormat.String, App);

        values.Should().ContainKey("missing");
        values["missing"].Should().Be("my-default", "an unresolved key falls back to what the caller declared, never to null");
    }

    [Fact]
    public async Task GetMany_returns_an_entry_for_every_requested_key()
    {
        // A caller should never have to handle a missing entry — that is a second failure mode for
        // no benefit, since it already told us what to use when nothing is stored.
        using var listener = StartListener(out var prefix, out _, bulkItems: [("k1", "v1")], singleKeyStatus: 404);
        var (_, content) = Build(prefix);

        var values = await content.GetManyContentAsync(
            [Req("k1"), Req("k2", "d2"), Req("k3", "d3")], Guid.Empty, ContentFormat.String, App);

        values.Keys.Should().BeEquivalentTo(["k1", "k2", "k3"]);
    }

    [Fact]
    public async Task GetMany_on_an_empty_set_calls_nothing()
    {
        using var listener = StartListener(out var prefix, out var state, bulkItems: []);
        var (_, content) = Build(prefix);

        var values = await content.GetManyContentAsync([], Guid.Empty, ContentFormat.String, App);

        values.Should().BeEmpty();
        state.BulkCalls.Should().Be(0);
        state.SingleKeyCalls.Should().Be(0);
    }

    // -------- Bounded materialization wait (#182) ---------------------------------------------

    [Fact]
    public async Task A_slow_first_materialization_returns_the_declared_default_instead_of_blocking()
    {
        // Measured at 1.5-3.3s per new key, which is what made a dialog take ~18s to open once and
        // 106ms every time after. The caller is holding the right text the whole time.
        using var listener = StartListener(out var prefix, out _, bulkItems: [], singleKeyDelay: TimeSpan.FromSeconds(2));
        var (_, content) = Build(prefix, wait: TimeSpan.FromMilliseconds(100));

        var sw = Stopwatch.StartNew();
        var result = await content.GetContentAsync("slow-key", "declared-default", Guid.Empty, ContentFormat.String, App);
        sw.Stop();

        result.Value.Should().Be("declared-default");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), "the caller must not wait out the server writing down an answer it supplied");
    }

    [Fact]
    public async Task The_value_still_materializes_after_the_caller_gave_up_waiting()
    {
        // The write is not abandoned — that would trade a slow first render for a key that never
        // gets created, which is worse. It finishes in the background and the next read is correct.
        using var listener = StartListener(out var prefix, out _, bulkItems: [], singleKeyDelay: TimeSpan.FromMilliseconds(400));
        var (_, content) = Build(prefix, wait: TimeSpan.FromMilliseconds(50));

        var first = await content.GetContentAsync("slow-key", "declared-default", Guid.Empty, ContentFormat.String, App);
        first.Value.Should().Be("declared-default");

        // Give the background materialization time to land, then read again.
        await WaitUntil(async () =>
        {
            var again = await content.GetContentAsync("slow-key", "declared-default", Guid.Empty, ContentFormat.String, App);
            return again.Value == "single-value";
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_zero_wait_restores_the_previous_blocking_behaviour()
    {
        // The escape hatch for a host that would rather block than paint a default it will replace.
        using var listener = StartListener(out var prefix, out _, bulkItems: [], singleKeyDelay: TimeSpan.FromMilliseconds(300));
        var (_, content) = Build(prefix, wait: TimeSpan.Zero);

        var result = await content.GetContentAsync("slow-key", "declared-default", Guid.Empty, ContentFormat.String, App);

        result.Value.Should().Be("single-value", "with the wait disabled the caller waits for the real value");
    }

    [Fact]
    public async Task A_fast_server_still_returns_the_stored_value()
    {
        // The wait must not cost correctness when the server is healthy, which is the normal case.
        using var listener = StartListener(out var prefix, out _, bulkItems: []);
        var (_, content) = Build(prefix, wait: TimeSpan.FromSeconds(2));

        var result = await content.GetContentAsync("k1", "declared-default", Guid.Empty, ContentFormat.String, App);

        result.Value.Should().Be("single-value");
    }

    private static ContentRequest Req(string key, string defaultValue = "def")
        => new() { Key = key, DefaultValue = defaultValue };

    private static async Task WaitUntil(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Condition was not met within the timeout.");
    }

    private static (IRemoteContentCallService remote, IContentService content) Build(string baseAddress, TimeSpan? wait = null)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    o.WarmUpEnabled = false;             // the tests warm explicitly, so counts stay readable
                    o.HttpTimeout = TimeSpan.FromSeconds(10);
                    if (wait.HasValue) o.MaterializationWait = wait.Value;
                });
            })
            .Build();

        return (host.Services.GetRequiredService<IRemoteContentCallService>(),
                host.Services.GetRequiredService<IContentService>());
    }

    private sealed class ListenerState
    {
        public int BulkCalls;
        public int SingleKeyCalls;
    }

    private static HttpListener StartListener(out string prefix, out ListenerState state,
        (string Key, string Value)[] bulkItems, int singleKeyStatus = 200, TimeSpan? singleKeyDelay = null)
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

                // A server from before batching answers the batch call 404, and the client then asks key
                // by key - what these tests were written against.
                if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url!.AbsolutePath.EndsWith("/batch", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    continue;
                }

                var segments = ctx.Request.Url!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var isBulk = segments.Length >= 3
                    && segments[0].Equals("Api", StringComparison.OrdinalIgnoreCase)
                    && segments[1].Equals("Content", StringComparison.OrdinalIgnoreCase)
                    && segments[2].Equals("all", StringComparison.OrdinalIgnoreCase);

                string body;
                if (isBulk)
                {
                    lock (sync) s.BulkCalls++;
                    ctx.Response.StatusCode = 200;
                    var items = string.Join(",", (bulkItems ?? []).Select(i =>
                        $$"""{"key":"{{i.Key}}","value":"{{i.Value}}"}"""));
                    body = $$"""{"items":[{{items}}],"validTo":"{{DateTime.UtcNow.AddHours(1):o}}"}""";
                }
                else
                {
                    lock (sync) s.SingleKeyCalls++;
                    // The delay stands in for the server materializing a key it has never seen —
                    // the write that the reported 1.5-3.3s was actually spent on.
                    if (singleKeyDelay.HasValue) await Task.Delay(singleKeyDelay.Value);
                    ctx.Response.StatusCode = singleKeyStatus;
                    body = $$"""{"value":"single-value","validTo":"{{DateTime.UtcNow.AddHours(1):o}}"}""";
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
                    // A caller that gave up on the bounded wait may already be gone. Not a failure.
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
