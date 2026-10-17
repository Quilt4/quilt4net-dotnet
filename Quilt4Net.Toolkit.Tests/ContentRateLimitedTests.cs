using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

// The Quilt4Net server is gaining a per-caller concurrency limiter, which converts what used to be a
// multi-minute hang into an immediate 429. That is only an improvement if the client understands it:
// a 429 must degrade to cache-or-default quietly and must honour the server's Retry-After rather than
// retrying straight back into the overload that produced it.
//
// Before this, a 429 fell into the generic non-2xx branch: logged at Error (one line per key per
// render under load) and negative-cached the caller's *default*, discarding a good cached value.
public class ContentRateLimitedTests
{
    [Fact]
    public async Task Rate_Limited_429_Falls_Back_To_Default_Without_Throwing()
    {
        using var listener = StartListener(out var prefix, out _, HttpStatusCode.TooManyRequests, retryAfterSeconds: 30);
        var service = BuildContentService(prefix, new RecordingLoggerProvider());

        var (value, success) = await service.GetContentAsync("Any.Key", "the-default", Guid.NewGuid(), ContentFormat.String, application: "App");

        value.Should().Be("the-default", "a rejected call must degrade to the caller's default, not surface an error");
        success.Should().BeFalse("the value did not come from the server");
    }

    [Fact]
    public async Task Rate_Limited_429_Is_Logged_At_Warning_Not_Error()
    {
        using var listener = StartListener(out var prefix, out _, HttpStatusCode.TooManyRequests, retryAfterSeconds: 30);
        var recorder = new RecordingLoggerProvider();
        var service = BuildContentService(prefix, recorder);

        await service.GetContentAsync("Any.Key", "the-default", Guid.NewGuid(), ContentFormat.String, application: "App");

        recorder.Entries.Should().NotContain(e => e.Level == LogLevel.Error,
            "backpressure is designed behaviour on both sides — logging it at Error makes a healthy shed look like an outage");
        recorder.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("429"),
            "a 429 is still worth seeing, at Warning");
    }

    [Fact]
    public async Task Rate_Limited_429_Does_Not_Re_Request_Before_Retry_After_Elapses()
    {
        // The property that stops the client deepening the overload that rejected it.
        using var listener = StartListener(out var prefix, out var requestCount, HttpStatusCode.TooManyRequests, retryAfterSeconds: 30);
        var service = BuildContentService(prefix, new RecordingLoggerProvider());
        var languageKey = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
            await service.GetContentAsync("Any.Key", "the-default", languageKey, ContentFormat.String, application: "App");

        requestCount().Should().Be(1,
            "Retry-After said 30s, so five renders inside that window must produce exactly one call");
    }

    [Fact]
    public async Task Rate_Limited_429_Keeps_A_Previously_Cached_Value_Instead_Of_The_Default()
    {
        // The user-visible half of Toolkit issue #172: overwriting a good Swedish value with the
        // English default makes server backpressure look like a translation regression.
        using var listener = StartSwitchableListener(out var prefix, out var mode);
        // Stale-while-revalidate off, so an expired entry takes the *foreground* refresh — the path
        // that used to overwrite it with the caller's default. With SWR on, the background path
        // already preserved the stale value.
        var service = BuildContentService(prefix, new RecordingLoggerProvider(), staleWhileRevalidate: false);
        var languageKey = Guid.NewGuid();

        // Already expired, so the very next resolve must go back to the server rather than be served
        // from cache — no sleeping in the test.
        mode.Serve("the-server-value", validFor: TimeSpan.FromSeconds(-1));
        var (first, firstSuccess) = await service.GetContentAsync("Any.Key", "the-default", languageKey, ContentFormat.String, application: "App");
        first.Should().Be("the-server-value");
        firstSuccess.Should().BeTrue();

        mode.RateLimit(retryAfterSeconds: 30);

        var (second, _) = await service.GetContentAsync("Any.Key", "the-default", languageKey, ContentFormat.String, application: "App");

        second.Should().Be("the-server-value",
            "a 429 says nothing about the content, so a value already held must survive it");
    }

    private static IContentService BuildContentService(string baseAddress, ILoggerProvider loggerProvider, bool staleWhileRevalidate = true)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    o.WarmUpEnabled = false;
                    o.StaleWhileRevalidate = staleWhileRevalidate;
                });
                services.AddLogging(b =>
                {
                    b.ClearProviders();
                    b.SetMinimumLevel(LogLevel.Debug);
                    b.AddProvider(loggerProvider);
                });
            })
            .Build();

        return host.Services.GetRequiredService<IContentService>();
    }

    private static HttpListener StartListener(out string prefix, out Func<int> requestCount, HttpStatusCode status, int? retryAfterSeconds = null)
    {
        var listener = Listen(out prefix);
        var hits = 0;
        requestCount = () => Volatile.Read(ref hits);

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

                Interlocked.Increment(ref hits);
                ctx.Response.StatusCode = (int)status;
                if (retryAfterSeconds != null)
                    ctx.Response.Headers["Retry-After"] = retryAfterSeconds.Value.ToString();
                ctx.Response.Close();
            }
        });

        return listener;
    }

    private static HttpListener StartSwitchableListener(out string prefix, out ResponseMode mode)
    {
        var listener = Listen(out prefix);
        var state = new ResponseMode();
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

                var (status, retryAfter, body, validFor) = state.Current;
                ctx.Response.StatusCode = (int)status;
                if (retryAfter != null) ctx.Response.Headers["Retry-After"] = retryAfter.Value.ToString();
                if (body != null)
                {
                    var json = $"{{\"value\":\"{body}\",\"validTo\":\"{DateTime.UtcNow.Add(validFor):O}\"}}";
                    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                }
                ctx.Response.Close();
            }
        });

        return listener;
    }

    private static HttpListener Listen(out string prefix)
    {
        var port = GetFreePort();
        prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
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
        private volatile Tuple<HttpStatusCode, int?, string, TimeSpan> _current =
            Tuple.Create(HttpStatusCode.OK, (int?)null, (string)null, TimeSpan.FromMinutes(10));

        public (HttpStatusCode Status, int? RetryAfter, string Body, TimeSpan ValidFor) Current =>
            (_current.Item1, _current.Item2, _current.Item3, _current.Item4);

        public void Serve(string body, TimeSpan? validFor = null) =>
            _current = Tuple.Create(HttpStatusCode.OK, (int?)null, body, validFor ?? TimeSpan.FromMinutes(10));

        public void RateLimit(int retryAfterSeconds) =>
            _current = Tuple.Create(HttpStatusCode.TooManyRequests, (int?)retryAfterSeconds, (string)null, TimeSpan.Zero);
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
