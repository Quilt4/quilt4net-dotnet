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

// Issue #131: RemoteContentCallService logged an Error for every missing-key fallback, flooding
// logs/telemetry. A key with no override (404) is the designed "use the caller's Default" path and
// must not log at Error; genuinely unexpected failures (5xx) must still log Error.
//
// Level revised from Debug to Information: an unseeded key is actionable (someone should seed it)
// and Debug is not enabled in practice, so it was effectively invisible. This does not reintroduce
// the #131 flood — the negative cache means it fires once per key per FailureCacheDuration rather
// than once per render (pinned by Missing_Key_404_Is_Negative_Cached_And_Not_Re_Requested below).
public class ContentNotFoundLoggingTests
{
    [Fact]
    public async Task Missing_Key_404_Is_Logged_At_Information_Not_Error()
    {
        using var listener = StartListener(out var prefix, out var requestCount, HttpStatusCode.NotFound);
        var recorder = new RecordingLoggerProvider();
        var service = BuildContentService(prefix, recorder);

        var (value, success) = await service.GetContentAsync("Missing.Key", "the-default", Guid.NewGuid(), ContentFormat.String, application: "App");

        value.Should().Be("the-default", "a missing key must fall back to the caller's default");
        success.Should().BeFalse("the value came from the default, not the server");

        recorder.Entries.Should().NotContain(e => e.Level == LogLevel.Error,
            "a missing key (404) is the designed fallback path and must not be logged at Error");
        recorder.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("Missing.Key"),
            "an unseeded key is actionable, and Debug is not enabled in practice — it must be visible at Information");
    }

    // The safety property that makes Information an acceptable level here. If this regresses, #131's
    // flood is back — a line per unseeded key per render, which is what FortDocs hit at ~290/session.
    [Fact]
    public async Task Missing_Key_404_Is_Logged_Once_Not_Once_Per_Render()
    {
        using var listener = StartListener(out var prefix, out _, HttpStatusCode.NotFound);
        var recorder = new RecordingLoggerProvider();
        var service = BuildContentService(prefix, recorder);
        var languageKey = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
            await service.GetContentAsync("Missing.Key", "the-default", languageKey, ContentFormat.String, application: "App");

        recorder.Entries.Count(e => e.Level == LogLevel.Information && e.Message.Contains("Missing.Key"))
            .Should().Be(1, "the negative cache must keep this to once per key per FailureCacheDuration, not once per render");
    }

    [Fact]
    public async Task Missing_Key_404_Is_Negative_Cached_And_Not_Re_Requested()
    {
        using var listener = StartListener(out var prefix, out var requestCount, HttpStatusCode.NotFound);
        var service = BuildContentService(prefix, new RecordingLoggerProvider());
        var languageKey = Guid.NewGuid();

        await service.GetContentAsync("Missing.Key", "the-default", languageKey, ContentFormat.String, application: "App");
        await service.GetContentAsync("Missing.Key", "the-default", languageKey, ContentFormat.String, application: "App");

        requestCount().Should().Be(1,
            "the 404 must be negative-cached so the same missing key isn't re-requested (and re-logged) on every render");
    }

    [Fact]
    public async Task Server_Error_500_Still_Logs_Error()
    {
        using var listener = StartListener(out var prefix, out var requestCount, HttpStatusCode.InternalServerError);
        var recorder = new RecordingLoggerProvider();
        var service = BuildContentService(prefix, recorder);

        var (value, _) = await service.GetContentAsync("Broken.Key", "the-default", Guid.NewGuid(), ContentFormat.String, application: "App");

        value.Should().Be("the-default");
        recorder.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("Broken.Key"),
            "an unexpected 5xx must remain an Error — only the expected 404 miss is demoted");
    }

    private static IContentService BuildContentService(string baseAddress, ILoggerProvider loggerProvider)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
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

    private static HttpListener StartListener(out string prefix, out Func<int> requestCount, HttpStatusCode status)
    {
        var port = GetFreePort();
        prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

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
