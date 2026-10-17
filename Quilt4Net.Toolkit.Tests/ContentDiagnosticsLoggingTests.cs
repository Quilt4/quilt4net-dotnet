using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

// Issue #132: opt-in diagnostics across the content/language load paths. Per-resolution Debug lines
// now carry the resolved language (so a spinning/looping content load can be traced by key+language),
// and a genuinely slow server round-trip surfaces as a single Warning — visible even without Debug
// logging — gated by ContentOptions.SlowLogThreshold (TimeSpan.Zero disables).
public class ContentDiagnosticsLoggingTests
{
    [Fact]
    public async Task Slow_content_load_logs_a_warning_when_threshold_exceeded()
    {
        using var listener = StartListener(out var prefix, HttpStatusCode.NotFound, TimeSpan.FromMilliseconds(80));
        var recorder = new RecordingLoggerProvider();
        var service = BuildContentService(prefix, recorder, o => o.SlowLogThreshold = TimeSpan.FromMilliseconds(1));

        await service.GetContentAsync("Slow.Key", "the-default", Guid.NewGuid(), ContentFormat.String, application: "App");

        recorder.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("Slow content load"),
            "a server round-trip slower than SlowLogThreshold must surface as a Warning even when Debug logging is off");
    }

    [Fact]
    public async Task Slow_content_load_warning_is_suppressed_when_threshold_is_zero()
    {
        using var listener = StartListener(out var prefix, HttpStatusCode.NotFound, TimeSpan.FromMilliseconds(80));
        var recorder = new RecordingLoggerProvider();
        var service = BuildContentService(prefix, recorder, o => o.SlowLogThreshold = TimeSpan.Zero);

        await service.GetContentAsync("Slow.Key", "the-default", Guid.NewGuid(), ContentFormat.String, application: "App");

        recorder.Entries.Should().NotContain(e => e.Message.Contains("Slow content load"),
            "TimeSpan.Zero must disable the slow-load warning");
    }

    [Fact]
    public async Task Resolution_debug_line_names_the_resolved_language()
    {
        var languageKey = Guid.NewGuid();
        using var listener = StartListener(out var prefix, HttpStatusCode.NotFound, TimeSpan.Zero);
        var recorder = new RecordingLoggerProvider();
        var service = BuildContentService(prefix, recorder, o => o.SlowLogThreshold = TimeSpan.Zero);

        await service.GetContentAsync("Some.Key", "the-default", languageKey, ContentFormat.String, application: "App");

        recorder.Entries.Should().Contain(e => e.Level == LogLevel.Debug
                && e.Message.Contains("Some.Key") && e.Message.Contains(languageKey.ToString()),
            "the per-resolution Debug line must carry the resolved language so a slow/looping load is traceable by key + language");
    }

    // Content was registered but no API key was supplied, so every value silently falls back to its
    // default. Warning, not Error — nothing failed and a key-less setup is valid for local dev — and
    // once per process, because the check runs on every single read.
    [Fact]
    public async Task Missing_api_key_logs_a_warning_once_not_per_read()
    {
        using var listener = StartListener(out var prefix, HttpStatusCode.NotFound, TimeSpan.Zero);
        var recorder = new RecordingLoggerProvider();
        var service = BuildContentService(prefix, recorder, o => o.ApiKey = "");

        for (var i = 0; i < 5; i++)
            await service.GetContentAsync("Some.Key", "the-default", Guid.NewGuid(), ContentFormat.String, application: "App");

        var warnings = recorder.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("API key"));
        warnings.Should().Be(1, "a missing API key is a startup misconfiguration, not a per-read event");
        recorder.Entries.Should().NotContain(e => e.Level == LogLevel.Error,
            "no API key is a misconfiguration, not a failure — the app still renders from defaults");
    }

    private static IContentService BuildContentService(string baseAddress, ILoggerProvider loggerProvider, Action<ContentOptions> configure)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    // Wait for the server's answer: these tests read what the round trip did, and the
                    // batch window plus the server delay can outlast the default wait.
                    o.MaterializationWait = TimeSpan.Zero;
                    configure(o);
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

    private static HttpListener StartListener(out string prefix, HttpStatusCode status, TimeSpan delay)
    {
        var port = GetFreePort();
        prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

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

                if (delay > TimeSpan.Zero) await Task.Delay(delay);
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
