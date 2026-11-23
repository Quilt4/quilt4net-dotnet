using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class FeatureToggleTests
{
    [Fact]
    public async Task GetToggleAsync_Returns_Fallback_When_Server_Returns_401()
    {
        // Arrange — start a local HTTP listener that always returns 401
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var listenerTask = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            ctx.Response.StatusCode = 401;
            ctx.Response.Close();
        }, TestContext.Current.CancellationToken);

        try
        {
            var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddQuilt4NetRemoteConfiguration(null, o =>
                    {
                        o.Quilt4NetAddress = prefix;
                        o.ApiKey = "test-key";
                    });
                })
                .Build();

            var service = host.Services.GetRequiredService<IFeatureToggleService>();

            // Act & Assert — should NOT throw, should return fallback
            var result = await service.GetToggleAsync("my-toggle", fallback: true);

            result.Should().BeTrue("fallback value should be returned when server returns 401");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetToggleAsync_Returns_Fallback_When_Server_Unreachable()
    {
        // Arrange — point at an address where nothing is listening
        var port = GetFreePort();
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetRemoteConfiguration(null, o =>
                {
                    o.Quilt4NetAddress = $"http://127.0.0.1:{port}/";
                    o.ApiKey = "test-key";
                });
            })
            .Build();

        var service = host.Services.GetRequiredService<IFeatureToggleService>();

        // Act & Assert — should NOT throw, should return fallback
        var result = await service.GetToggleAsync("my-toggle", fallback: true);

        result.Should().BeTrue("fallback value should be returned when server is unreachable");
    }

    [Fact]
    public async Task GetToggleAsync_Returns_Fallback_When_Server_Returns_500()
    {
        // Arrange — start a local HTTP listener that returns 500
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var listenerTask = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            ctx.Response.StatusCode = 500;
            ctx.Response.Close();
        }, TestContext.Current.CancellationToken);

        try
        {
            var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddQuilt4NetRemoteConfiguration(null, o =>
                    {
                        o.Quilt4NetAddress = prefix;
                        o.ApiKey = "test-key";
                    });
                })
                .Build();

            var service = host.Services.GetRequiredService<IFeatureToggleService>();

            // Act & Assert
            var result = await service.GetToggleAsync("my-toggle", fallback: false);

            result.Should().BeFalse("fallback value should be returned when server returns 500");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetToggleAsync_Same_Key_Different_Application_Are_Separate_Cache_Entries()
    {
        // Arrange — local listener that counts requests and returns the request's
        // base64-encoded complex key in the body so we can verify the second call
        // actually hit the server (different application = different cache entry).
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var hits = 0;
        var receivedApplications = new System.Collections.Concurrent.ConcurrentBag<string>();
        var listenerTask = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { return; }
                Interlocked.Increment(ref hits);

                // URL is /Api/Configuration/{base64(complexKey)} — decode the request to capture which Application was sent.
                var segments = ctx.Request.Url!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 3)
                {
                    var encoded = WebUtility.UrlDecode(segments[2]);
                    var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Application", out var appProp))
                        receivedApplications.Add(appProp.GetString());
                }

                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                var body = $$"""{"value":"True","validTo":"{{DateTime.UtcNow.AddHours(1):o}}"}""";
                var buf = System.Text.Encoding.UTF8.GetBytes(body);
                await ctx.Response.OutputStream.WriteAsync(buf);
                ctx.Response.Close();
            }
        }, TestContext.Current.CancellationToken);

        try
        {
            var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddQuilt4NetRemoteConfiguration(null, o =>
                    {
                        o.Quilt4NetAddress = prefix;
                        o.ApiKey = "test-key";
                    });
                })
                .Build();

            var service = host.Services.GetRequiredService<IRemoteConfigurationService>();

            // Act — same key, two different applications
            await service.GetAsync("my-toggle", false, application: "App1");
            await service.GetAsync("my-toggle", false, application: "App2");

            // Assert — both calls must hit the server because the cache key
            // includes the effective application. With a key-only cache, the
            // second call would silently return the first call's cached value.
            hits.Should().Be(2,
                "same toggle key with different application is a different cache entry — neither call may use the other's cached value");
            receivedApplications.Should().BeEquivalentTo(["App1", "App2"]);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static int GetFreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task GetToggleAsync_Logs_Resolution_Result_At_Debug_Not_Information()
    {
        // Issue #108: the per-resolution "Configuration '{Key}' resolved … Source: …" lines fired
        // on every flag check and dwarfed any actionable telemetry (~449k Information traces/week
        // per app at Eplicta, ~99% Cache hits). They must log at Debug now so Application Insights
        // (default Information threshold) doesn't ingest them. Errors / warnings / background-
        // refresh value-change lines stay at their original levels.
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var listenerTask = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { return; }
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                var body = $$"""{"value":"True","validTo":"{{DateTime.UtcNow.AddHours(1):o}}"}""";
                var buf = System.Text.Encoding.UTF8.GetBytes(body);
                await ctx.Response.OutputStream.WriteAsync(buf);
                ctx.Response.Close();
            }
        }, TestContext.Current.CancellationToken);

        var recorder = new RecordingLoggerProvider();
        try
        {
            var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddQuilt4NetRemoteConfiguration(null, o =>
                    {
                        o.Quilt4NetAddress = prefix;
                        o.ApiKey = "test-key";
                    });
                    services.AddLogging(b =>
                    {
                        b.ClearProviders();
                        b.SetMinimumLevel(LogLevel.Debug);
                        b.AddProvider(recorder);
                    });
                })
                .Build();

            var service = host.Services.GetRequiredService<IFeatureToggleService>();

            await service.GetToggleAsync("my-toggle", fallback: false);
            await service.GetToggleAsync("my-toggle", fallback: false); // second call: cache hit

            var resolvedEntries = recorder.Entries
                .Where(e => e.Message.Contains("resolved in") && e.Message.Contains("Source:"))
                .ToArray();

            resolvedEntries.Should().NotBeEmpty("the resolution-result line should still be logged, just at Debug");
            resolvedEntries.Should().OnlyContain(e => e.Level == LogLevel.Debug,
                "per-resolution result lines must be Debug to avoid flooding Application Insights");
        }
        finally
        {
            listener.Stop();
        }
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
