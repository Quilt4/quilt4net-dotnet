using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Features.Content;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

// WarmUpLanguages: WarmConfiguredLanguagesAsync warms the default language plus each language named
// in ContentOptions.WarmUpLanguages (resolved name -> key against the server's language list), so a
// site can hot-load e.g. English + Svenska at startup instead of only the default.
public class ContentWarmUpLanguagesTests
{
    private static readonly Guid SvenskaKey = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Warms_default_plus_each_named_language()
    {
        using var listener = StartListener(out var prefix, out var warmed, out _);
        var warm = Build(prefix, warmUpLanguages: ["Svenska"]);

        await warm.WarmConfiguredLanguagesAsync();

        warmed.Should().Contain(Guid.Empty, "the default language is always warmed");
        warmed.Should().Contain(SvenskaKey, "a configured language is resolved by name and warmed");
    }

    [Fact]
    public async Task Default_only_when_no_languages_configured()
    {
        using var listener = StartListener(out var prefix, out var warmed, out _);
        var warm = Build(prefix, warmUpLanguages: []);

        await warm.WarmConfiguredLanguagesAsync();

        warmed.Should().ContainSingle().Which.Should().Be(Guid.Empty,
            "with no WarmUpLanguages, only the default language warms — the previous behaviour");
    }

    [Fact]
    public async Task Unknown_language_name_is_skipped_with_a_warning()
    {
        using var listener = StartListener(out var prefix, out var warmed, out _);
        var recorder = new RecordingLoggerProvider();
        var warm = Build(prefix, warmUpLanguages: ["Klingon"], recorder);

        await warm.WarmConfiguredLanguagesAsync();

        warmed.Should().ContainSingle().Which.Should().Be(Guid.Empty, "an unmatched name warms nothing extra");
        recorder.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("Klingon"),
            "a configured name with no matching server language must be reported");
    }

    private static IRemoteContentCallService Build(string baseAddress, IReadOnlyList<string> warmUpLanguages, ILoggerProvider loggerProvider = null)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    o.WarmUpLanguages = warmUpLanguages;
                });
                if (loggerProvider != null)
                    services.AddLogging(b => { b.ClearProviders(); b.SetMinimumLevel(LogLevel.Debug); b.AddProvider(loggerProvider); });
            })
            .Build();

        return host.Services.GetRequiredService<IRemoteContentCallService>();
    }

    private static HttpListener StartListener(out string prefix, out ConcurrentBag<Guid> warmedLanguages, out ConcurrentBag<string> languageCalls)
    {
        var port = GetFreePort();
        prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var warmed = new ConcurrentBag<Guid>();
        var langCalls = new ConcurrentBag<string>();
        warmedLanguages = warmed;
        languageCalls = langCalls;

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
                string body;

                if (segments.Length >= 2 && segments[1].Equals("Language", StringComparison.OrdinalIgnoreCase))
                {
                    // Api/Language/{app}/{env} — return the server's language list (LanguageResponse
                    // wrapper: { languages, validTo }, not a bare array).
                    langCalls.Add(ctx.Request.Url.AbsolutePath);
                    body = $$"""{"languages":[{"key":"{{Guid.Empty}}","name":"English"},{"key":"{{SvenskaKey}}","name":"Svenska"}],"validTo":"{{DateTime.UtcNow.AddHours(1):o}}"}""";
                }
                else if (segments.Length >= 3 && segments[1].Equals("Content", StringComparison.OrdinalIgnoreCase)
                         && segments[2].Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    // Api/Content/all/{app}/{env}/{languageKey} — record which language was warmed.
                    if (Guid.TryParse(segments[^1], out var langKey)) warmed.Add(langKey);
                    body = $$"""{"items":[],"validTo":"{{DateTime.UtcNow.AddHours(1):o}}"}""";
                }
                else
                {
                    body = "{}";
                }

                ctx.Response.StatusCode = 200;
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

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Entries);
        public void Dispose() { }

        private sealed class RecordingLogger(List<(LogLevel, string)> entries) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                lock (entries) entries.Add((logLevel, formatter(state, exception)));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
