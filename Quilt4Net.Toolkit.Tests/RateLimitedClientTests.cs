using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// The server now sheds load per caller and answers with <c>429</c> plus <c>Retry-After</c>. Shedding
/// is only an improvement if the client understands it: the content single-key path has since 1.0.6,
/// but the configuration client and the bulk warm-up did not, which left two ways for backpressure to
/// be misread as a fault.
/// </summary>
public class RateLimitedClientTests
{
    [Fact]
    public async Task Configuration_treats_a_429_as_backpressure_not_an_error()
    {
        // Logging a shed at Error makes a healthy server look like an outage, and under load it is the
        // single most repeated line there is.
        using var listener = StartListener(out var prefix, out var mode, out _);
        mode.RateLimit(retryAfterSeconds: 30);
        var recorder = new RecordingLoggerProvider();
        var service = BuildToggleService(prefix, recorder);

        await service.GetToggleAsync("AssistantPanel.Enabled", fallback: false);

        recorder.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
        recorder.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("429"));
    }

    [Fact]
    public async Task Configuration_honours_Retry_After_instead_of_its_own_back_off()
    {
        // The property that stops the client deepening the overload that rejected it.
        //
        // The failure interval is deliberately set far SHORTER than Retry-After, because that is the
        // only arrangement in which the two can be told apart: with the default 5s back-off and a
        // 30s Retry-After, a handful of rapid reads sit inside both windows and the test passes
        // whether or not the header is read at all. Mutation-checked — removing the Retry-After
        // override makes this fail.
        using var listener = StartListener(out var prefix, out var mode, out var requestCount);
        mode.RateLimit(retryAfterSeconds: 30);
        var service = BuildToggleService(prefix, new RecordingLoggerProvider(),
            o => { o.FailureCacheDuration = TimeSpan.FromMilliseconds(50); o.MaxFailureCacheDuration = TimeSpan.FromMilliseconds(50); });

        await service.GetToggleAsync("AssistantPanel.Enabled", fallback: false);
        await Task.Delay(400, TestContext.Current.CancellationToken);
        await service.GetToggleAsync("AssistantPanel.Enabled", fallback: false);

        requestCount().Should().Be(1,
            "the server asked for 30s, so the client must wait that long and not fall back to its own 50ms interval");
    }

    [Fact]
    public async Task Configuration_still_returns_the_fallback_without_throwing()
    {
        using var listener = StartListener(out var prefix, out var mode, out _);
        mode.RateLimit(retryAfterSeconds: 30);
        var service = BuildToggleService(prefix, new RecordingLoggerProvider());

        var value = await service.GetToggleAsync("AssistantPanel.Enabled", fallback: true);

        value.Should().BeTrue("a shed call must degrade to the caller's fallback, not surface an error");
    }

    [Fact]
    public async Task A_rate_limited_warm_up_retries_rather_than_falling_back_to_per_key_fetching()
    {
        // The fallback loop: WarmCacheAsync swallowing a 429 drops the whole language to per-key
        // fetching, turning one shed call into hundreds — the exact burst the server shed it to avoid.
        using var listener = StartListener(out var prefix, out var mode, out var requestCount);
        mode.RateLimit(retryAfterSeconds: 1);
        var recorder = new RecordingLoggerProvider();
        var remote = BuildRemoteContentService(prefix, recorder);

        // The listener starts serving content again while the client is waiting out the Retry-After.
        var flip = Task.Run(async () =>
        {
            await Task.Delay(300);
            mode.ServeBulk();
        });

        await remote.WarmCacheAsync(Guid.Empty, application: "App");
        await flip;

        requestCount().Should().BeGreaterThan(1, "the warm-up must try again after Retry-After rather than give up");
        recorder.Entries.Should().Contain(e => e.Message.Contains("Retrying in"),
            "and it must say so, since silently falling back to per-key fetching is what caused the storm");
    }

    [Fact]
    public async Task A_long_Retry_After_is_left_to_the_periodic_re_warm()
    {
        // Bounded on purpose: a background task parked for minutes is harder to reason about than one
        // that simply tries again on the next tick.
        using var listener = StartListener(out var prefix, out var mode, out var requestCount);
        mode.RateLimit(retryAfterSeconds: 600);
        var recorder = new RecordingLoggerProvider();
        var remote = BuildRemoteContentService(prefix, recorder);

        await remote.WarmCacheAsync(Guid.Empty, application: "App");

        requestCount().Should().Be(1, "a ten-minute wait is not worth holding a task open for");
        recorder.Entries.Should().Contain(e => e.Message.Contains("next periodic re-warm"));
    }

    private static IFeatureToggleService BuildToggleService(string baseAddress, ILoggerProvider loggerProvider, Action<RemoteConfigurationOptions> configure = null)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetRemoteConfiguration(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    o.StaleWhileRevalidate = false;
                    configure?.Invoke(o);
                });
                services.AddLogging(b => { b.ClearProviders(); b.SetMinimumLevel(LogLevel.Debug); b.AddProvider(loggerProvider); });
            })
            .Build();

        return host.Services.GetRequiredService<IFeatureToggleService>();
    }

    private static IRemoteContentCallService BuildRemoteContentService(string baseAddress, ILoggerProvider loggerProvider)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    o.WarmUpEnabled = false;
                });
                services.AddLogging(b => { b.ClearProviders(); b.SetMinimumLevel(LogLevel.Debug); b.AddProvider(loggerProvider); });
            })
            .Build();

        return host.Services.GetRequiredService<IRemoteContentCallService>();
    }

    private static HttpListener StartListener(out string prefix, out ResponseMode mode, out Func<int> requestCount)
    {
        var port = GetFreePort();
        prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var state = new ResponseMode();
        mode = state;
        var hits = 0;
        requestCount = () => Volatile.Read(ref hits);

        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { return; }

                Interlocked.Increment(ref hits);
                var (status, retryAfter, body) = state.Current;
                ctx.Response.StatusCode = (int)status;
                if (retryAfter != null) ctx.Response.Headers["Retry-After"] = retryAfter.Value.ToString();
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

    private sealed class ResponseMode
    {
        private volatile Tuple<HttpStatusCode, int?, string> _current = Tuple.Create(HttpStatusCode.OK, (int?)null, (string)null);

        public (HttpStatusCode Status, int? RetryAfter, string Body) Current => (_current.Item1, _current.Item2, _current.Item3);

        public void RateLimit(int retryAfterSeconds) =>
            _current = Tuple.Create(HttpStatusCode.TooManyRequests, (int?)retryAfterSeconds, (string)null);

        public void ServeBulk() =>
            _current = Tuple.Create(HttpStatusCode.OK, (int?)null, $"{{\"items\":[],\"validTo\":\"{DateTime.UtcNow.AddMinutes(10):O}\"}}");
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Entries);
        public void Dispose() { }

        private sealed class RecordingLogger : ILogger
        {
            private readonly List<(LogLevel, string)> _entries;
            public RecordingLogger(List<(LogLevel, string)> entries) => _entries = entries;
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                Func<TState, Exception, string> formatter)
            {
                lock (_entries) _entries.Add((logLevel, formatter(state, exception)));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
