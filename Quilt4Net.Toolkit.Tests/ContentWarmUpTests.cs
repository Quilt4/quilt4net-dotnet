using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class ContentWarmUpTests
{
    private const string App = "App1";

    [Fact]
    public async Task WarmCacheAsync_then_GetContentAsync_serves_from_cache_without_a_per_key_call()
    {
        using var listener = StartListener(out var prefix, out var state,
            bulkItems: [("k1", "v1"), ("k2", "v2")]);

        var (warm, content) = Build(prefix);

        await warm.WarmCacheAsync(Guid.Empty, App);
        state.SingleKeyCalls = 0; // ignore anything before the warm; measure reads after it

        var r1 = await content.GetContentAsync("k1", "def", Guid.Empty, ContentFormat.String, App);
        var r2 = await content.GetContentAsync("k2", "def", Guid.Empty, ContentFormat.String, App);

        r1.Should().Be(("v1", true));
        r2.Should().Be(("v2", true));
        state.SingleKeyCalls.Should().Be(0, "warmed keys must be served from cache without a server round-trip");
        state.BulkCalls.Should().Be(1, "warm-up is a single bulk call for the whole application");
    }

    [Fact]
    public async Task The_cache_status_says_what_each_language_holds_and_how_its_last_bulk_load_went()
    {
        using var listener = StartListener(out var prefix, out _, bulkItems: [("menu.home", "Home"), ("menu.about", "About")]);
        var (warm, _) = Build(prefix);

        await warm.WarmCacheAsync(Guid.Empty, App);

        var language = warm.GetCacheStatus().Should().ContainSingle().Subject;
        language.Count.Should().Be(2);
        language.Defaults.Should().Be(0);
        language.FreshUntil.Should().NotBeNull();
        language.LastWarmUp.Should().Match<ContentWarmUp>(x => x.Succeeded && x.Items == 2 && x.Problem == null);
        warm.GetCachedEntries().Select(x => (x.Key, x.Application, x.Value)).Should().BeEquivalentTo([("menu.home", App, "Home"), ("menu.about", App, "About")]);
    }

    [Fact]
    public async Task A_failed_bulk_load_shows_in_the_cache_status_with_its_reason()
    {
        using var listener = StartListener(out var prefix, out _, bulkItems: null, bulkStatus: 500);
        var (warm, _) = Build(prefix);

        await warm.WarmCacheAsync(Guid.Empty, App);

        warm.GetCacheStatus().Should().ContainSingle().Which.LastWarmUp.Should().Match<ContentWarmUp>(x => !x.Succeeded && x.Problem.StartsWith("500"));
    }

    [Fact]
    public async Task WarmCacheAsync_falls_back_to_per_key_when_bulk_endpoint_returns_404()
    {
        using var listener = StartListener(out var prefix, out var state, bulkItems: null, bulkStatus: 404);

        var (warm, content) = Build(prefix);

        await warm.WarmCacheAsync(Guid.Empty, App); // must not throw
        state.SingleKeyCalls = 0;

        var r = await content.GetContentAsync("k1", "def", Guid.Empty, ContentFormat.String, App);

        r.Success.Should().BeTrue();
        r.Value.Should().Be("single-value");
        state.SingleKeyCalls.Should().Be(1, "an old server without the bulk endpoint must leave the per-key path working");
    }

    // #155: warm-up runs once per configured language, so a failure line naming neither the
    // application nor the language cannot say which of them dropped to per-key fetching — which is
    // exactly what FortDocs saw when the Swedish warm-up 500'd and the English one succeeded. The
    // success line has always named them; the failure line was the odd one out.

    [Fact]
    public async Task A_failed_warm_up_names_the_application_and_language_it_was_warming()
    {
        var swedish = Guid.NewGuid();
        using var listener = StartListener(out var prefix, out _, bulkItems: null, bulkStatus: 500);
        var logs = new CapturingLoggerProvider();

        var (warm, _) = Build(prefix, loggerProvider: logs);
        await warm.WarmCacheAsync(swedish, App);

        var entry = logs.Entries.Should().ContainSingle(x => x.Contains("Content warm-up failed")).Subject;
        entry.Should().Contain(App).And.Contain(swedish.ToString());
    }

    [Fact]
    public async Task A_failed_warm_up_reports_the_response_body_as_the_reason()
    {
        // "The 500 should say something." Without the body the only signal is the status code,
        // which says a request failed but never why.
        using var listener = StartListener(out var prefix, out _, bulkItems: null, bulkStatus: 500);
        var logs = new CapturingLoggerProvider();

        var (warm, _) = Build(prefix, loggerProvider: logs);
        await warm.WarmCacheAsync(Guid.NewGuid(), App);

        logs.Entries.Should().ContainSingle(x => x.Contains("Content warm-up failed") && x.Contains("Body:"));
    }

    [Fact]
    public async Task GetCacheCountsByLanguage_reports_loaded_count_per_language()
    {
        using var listener = StartListener(out var prefix, out _, bulkItems: [("k1", "v1"), ("k2", "v2")]);

        var (warm, _) = Build(prefix);
        await warm.WarmCacheAsync(Guid.Empty, App);

        var counts = warm.GetCacheCountsByLanguage();

        counts.Should().ContainKey(Guid.Empty);
        counts[Guid.Empty].Should().Be(2);
    }

    [Fact]
    public async Task WarmCacheAsync_does_nothing_when_no_api_key_configured()
    {
        using var listener = StartListener(out var prefix, out var state, bulkItems: [("k1", "v1")]);

        var (warm, _) = Build(prefix, apiKey: "");

        await warm.WarmCacheAsync(Guid.Empty, App);

        state.BulkCalls.Should().Be(0, "no API key means no calls are made at all");
    }

    // Warm-up is fire-and-forget and runs concurrently with the per-key reads a language switch
    // triggers, so a slower bulk response could overwrite what those reads cached — including a
    // value the server produced *because* of them, such as a translation backfilled on first
    // request (Quilt4Net.Server, Toolkit #152). The user then saw the old language for the rest of
    // the TTL, intermittently, depending on which response landed last.

    [Fact]
    public async Task Warm_up_does_not_overwrite_a_newer_cached_value()
    {
        // Bulk response is deliberately older than the per-key entry already in the cache: it is a
        // snapshot from before that read happened.
        using var listener = StartListener(out var prefix, out _,
            bulkItems: [("k1", "stale-from-bulk")], bulkTtl: TimeSpan.FromMinutes(30));

        var (warm, content) = Build(prefix);

        // Per-key read first — caches "single-value" with the longer TTL.
        await content.GetContentAsync("k1", "def", Guid.Empty, ContentFormat.String, App);

        await warm.WarmCacheAsync(Guid.Empty, App);

        var after = await content.GetContentAsync("k1", "def", Guid.Empty, ContentFormat.String, App);
        after.Value.Should().Be("single-value", "the bulk snapshot predates the per-key read and must not undo it");
    }

    [Fact]
    public async Task Warm_up_does_replace_an_older_cached_value()
    {
        // The converse — the guard must not freeze the cache. A warm-up newer than what is cached
        // is a legitimate refresh.
        using var listener = StartListener(out var prefix, out _,
            bulkItems: [("k1", "fresh-from-bulk")], bulkTtl: TimeSpan.FromHours(2));

        var (warm, content) = Build(prefix);
        await content.GetContentAsync("k1", "def", Guid.Empty, ContentFormat.String, App);

        await warm.WarmCacheAsync(Guid.Empty, App);

        var after = await content.GetContentAsync("k1", "def", Guid.Empty, ContentFormat.String, App);
        after.Value.Should().Be("fresh-from-bulk");
    }

    [Fact]
    public async Task Warm_up_replaces_a_negative_cache_entry_whatever_its_timestamp()
    {
        // A negative entry's ValidTo comes from FailureCacheDuration, not a real response, so it can
        // outlast a genuine warm-up value. Real content must still win.
        using var listener = StartListener(out var prefix, out _,
            bulkItems: [("k1", "real-value")], bulkTtl: TimeSpan.FromMinutes(1), singleKeyStatus: 500);

        var (warm, content) = Build(prefix);

        // Fails -> negative-cached with the caller's default and a 10-minute FailureCacheDuration.
        var failed = await content.GetContentAsync("k1", "def", Guid.Empty, ContentFormat.String, App);
        failed.Value.Should().Be("def");

        await warm.WarmCacheAsync(Guid.Empty, App);

        var after = await content.GetContentAsync("k1", "def", Guid.Empty, ContentFormat.String, App);
        after.Value.Should().Be("real-value", "a placeholder default must never outrank confirmed server content");
    }

    private static (IRemoteContentCallService warm, IContentService content) Build(string baseAddress, string apiKey = "test-key", ILoggerProvider loggerProvider = null)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                if (loggerProvider != null) services.AddLogging(b => b.AddProvider(loggerProvider).SetMinimumLevel(LogLevel.Debug));
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = apiKey;
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

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<string> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Capturing(Entries);
        public void Dispose() { }

        private sealed class Capturing(ConcurrentBag<string> entries) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
                => entries.Add(formatter(state, exception));
        }
    }

    private static HttpListener StartListener(out string prefix, out ListenerState state,
        (string Key, string Value)[] bulkItems, int bulkStatus = 200, TimeSpan? bulkTtl = null, int singleKeyStatus = 200)
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
                    ctx.Response.StatusCode = bulkStatus;
                    var items = string.Join(",", (bulkItems ?? []).Select(i =>
                        $$"""{"key":"{{i.Key}}","value":"{{i.Value}}"}"""));
                    // The bulk TTL is adjustable so a test can make the warm-up response older than
                    // an entry already in the cache — the snapshot-vs-per-key race this guards.
                    body = $$"""{"items":[{{items}}],"validTo":"{{DateTime.UtcNow.Add(bulkTtl ?? TimeSpan.FromHours(1)):o}}"}""";
                }
                else
                {
                    lock (sync) s.SingleKeyCalls++;
                    ctx.Response.StatusCode = singleKeyStatus;
                    body = $$"""{"value":"single-value","validTo":"{{DateTime.UtcNow.AddHours(1):o}}"}""";
                }

                ctx.Response.ContentType = "application/json";
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
