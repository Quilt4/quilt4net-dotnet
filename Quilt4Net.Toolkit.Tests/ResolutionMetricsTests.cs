using System.Diagnostics.Metrics;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Issue #170: resolution was instrumented only as `Debug` logs, so a host could see *that* one call
/// was slow and never how many calls there were, nor what fraction hit the cache — the number that
/// identifies a fault like #174 on first look. These pin that both clients publish it, and that the
/// tag sets differ deliberately.
/// </summary>
public class ResolutionMetricsTests
{
    [Fact]
    public async Task Content_records_a_resolution_tagged_with_its_source()
    {
        using var listener = StartListener(out var prefix, out var mode);
        mode.Serve("{\"value\":\"from-server\",\"validTo\":\"" + Iso(TimeSpan.FromMinutes(10)) + "\"}");
        var app = UniqueApp();
        using var collector = new MeasurementCollector(Quilt4NetMetrics.ContentMeterName, Quilt4NetMetrics.ContentResolutions, app);
        var content = BuildContentService(prefix, _ => { });
        var languageKey = Guid.NewGuid();

        await content.GetContentAsync("Any.Key", "the-default", languageKey, ContentFormat.String, application: app);
        await content.GetContentAsync("Any.Key", "the-default", languageKey, ContentFormat.String, application: app);

        var sources = collector.TagValues("source");
        sources.Should().Contain("Server", "the first read reached the server");
        sources.Should().Contain("Cache", "the second read was served from cache — this is what makes a hit ratio computable");
    }

    [Fact]
    public async Task Content_does_not_tag_the_key()
    {
        // The key is unbounded — 1,283 in one reported application — so tagging it would blow up
        // cardinality. Per-key volume stays in the Debug log, which already carries it.
        using var listener = StartListener(out var prefix, out var mode);
        mode.Serve("{\"value\":\"v\",\"validTo\":\"" + Iso(TimeSpan.FromMinutes(10)) + "\"}");
        var app = UniqueApp();
        using var collector = new MeasurementCollector(Quilt4NetMetrics.ContentMeterName, Quilt4NetMetrics.ContentResolutions, app);
        var content = BuildContentService(prefix, _ => { });

        await content.GetContentAsync("Some.Key", "d", Guid.NewGuid(), ContentFormat.String, application: app);

        collector.TagNames().Should().NotContain("key");
        collector.TagNames().Should().Contain(new[] { "source", "application", "language", "stale" });
    }

    [Fact]
    public async Task Content_metrics_can_be_turned_off()
    {
        using var listener = StartListener(out var prefix, out var mode);
        mode.Serve("{\"value\":\"v\",\"validTo\":\"" + Iso(TimeSpan.FromMinutes(10)) + "\"}");
        var app = UniqueApp();
        using var collector = new MeasurementCollector(Quilt4NetMetrics.ContentMeterName, Quilt4NetMetrics.ContentResolutions, app);
        var content = BuildContentService(prefix, o => o.MetricsEnabled = false);

        await content.GetContentAsync("Some.Key", "d", Guid.NewGuid(), ContentFormat.String, application: app);

        collector.Count.Should().Be(0);
    }

    [Fact]
    public async Task Configuration_records_a_fallback_tagged_with_the_key()
    {
        // The key IS a tag here: a host has a handful of toggles, and "which toggle is falling back"
        // is exactly the question. This is also the shape that would have identified #174 on sight —
        // a counter reading 0% Server for one key over fourteen days.
        using var listener = StartListener(out var prefix, out var mode);
        mode.Fail(HttpStatusCode.InternalServerError);
        var app = UniqueApp();
        using var collector = new MeasurementCollector(Quilt4NetMetrics.ConfigurationMeterName, Quilt4NetMetrics.ConfigurationResolutions, app);
        var toggles = BuildToggleService(prefix, o => o.Application = app);

        await toggles.GetToggleAsync("AssistantPanel.Enabled", fallback: false);

        collector.Count.Should().BeGreaterThan(0);
        collector.TagValues("source").Should().Contain("Fallback");
        collector.TagValues("key").Should().Contain("AssistantPanel.Enabled");
    }

    [Fact]
    public async Task Configuration_records_a_duration()
    {
        using var listener = StartListener(out var prefix, out var mode);
        mode.Serve("{\"value\":\"True\",\"validTo\":\"" + Iso(TimeSpan.FromMinutes(10)) + "\"}");
        var app = UniqueApp();
        using var collector = new MeasurementCollector(Quilt4NetMetrics.ConfigurationMeterName, Quilt4NetMetrics.ConfigurationResolutionDuration, app);
        var toggles = BuildToggleService(prefix, o => o.Application = app);

        await toggles.GetToggleAsync("AssistantPanel.Enabled", fallback: false);

        collector.Count.Should().BeGreaterThan(0, "latency per source is half the point of the histogram");
    }

    [Fact]
    public async Task Configuration_metrics_can_be_turned_off()
    {
        using var listener = StartListener(out var prefix, out var mode);
        mode.Fail(HttpStatusCode.InternalServerError);
        var app = UniqueApp();
        using var collector = new MeasurementCollector(Quilt4NetMetrics.ConfigurationMeterName, Quilt4NetMetrics.ConfigurationResolutions, app);
        var toggles = BuildToggleService(prefix, o => { o.MetricsEnabled = false; o.Application = app; });

        await toggles.GetToggleAsync("AssistantPanel.Enabled", fallback: false);

        collector.Count.Should().Be(0);
    }

    // Short on purpose: the content request is base64-encoded into a single URL segment, and Http.sys
    // rejects a segment over 260 characters with a bare 400 that reads as a server bug.
    private static string UniqueApp() => "a" + Guid.NewGuid().ToString("N")[..6];

    private static string Iso(TimeSpan offset) => DateTime.UtcNow.Add(offset).ToString("O");

    private static IContentService BuildContentService(string baseAddress, Action<ContentOptions> configure)
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

        return host.Services.GetRequiredService<IContentService>();
    }

    private static IFeatureToggleService BuildToggleService(string baseAddress, Action<RemoteConfigurationOptions> configure)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetRemoteConfiguration(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    configure(o);
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

                // A server from before batching answers the batch call 404, and the client then asks key
                // by key - what these tests were written against.
                if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url!.AbsolutePath.EndsWith("/batch", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    continue;
                }

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

    /// <summary>
    /// Captures measurements for one instrument by name. Uses <see cref="MeterListener"/> directly so
    /// the assertions run against the same surface a host's OpenTelemetry exporter subscribes to,
    /// rather than a test-only abstraction over it.
    /// </summary>
    private sealed class MeasurementCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<Dictionary<string, string>> _tagSets = [];
        private readonly string _applicationFilter;

        public MeasurementCollector(string meterName, string instrumentName, string applicationFilter = null)
        {
            _applicationFilter = applicationFilter;
            _listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == meterName && instrument.Name == instrumentName)
                    l.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) => Record(tags));
            _listener.SetMeasurementEventCallback<double>((_, _, tags, _) => Record(tags));
            _listener.Start();
        }

        public int Count { get { lock (_tagSets) return _tagSets.Count; } }

        public IReadOnlyList<string> TagValues(string tagName)
        {
            lock (_tagSets) return _tagSets.Where(t => t.ContainsKey(tagName)).Select(t => t[tagName]).ToList();
        }

        public IReadOnlyList<string> TagNames()
        {
            lock (_tagSets) return _tagSets.SelectMany(t => t.Keys).Distinct().ToList();
        }

        private void Record(ReadOnlySpan<KeyValuePair<string, object>> tags)
        {
            var set = new Dictionary<string, string>();
            foreach (var tag in tags) set[tag.Key] = $"{tag.Value}";
            // The meter name is process-global, so a collector would otherwise pick up measurements
            // from services built by tests running in parallel. The application tag is what makes one
            // test observable in isolation.
            if (_applicationFilter != null && set.GetValueOrDefault("application") != _applicationFilter) return;
            lock (_tagSets) _tagSets.Add(set);
        }

        public void Dispose() => _listener.Dispose();
    }
}
