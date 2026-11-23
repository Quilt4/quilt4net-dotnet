using System.Collections.Concurrent;
using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Features.ReleaseNotes;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// The lookup half of release notes — how a consuming application asks Quilt4Net.Server what has
/// changed since the version it last showed this user. Publishing is not here on purpose: notes are
/// written over plain REST or MCP from a release pipeline, which needs no client.
/// </summary>
public class ReleaseNoteServiceTests
{
    private const string TwoNotes =
        """
        {
          "sinceVersion": "1.0.0",
          "latestVersion": "1.2.0",
          "notes": [
            { "version": "1.2.0", "isPreRelease": false, "publishedUtc": "2026-09-20T10:00:00Z", "markdown": "## 1.2.0\nFaster." },
            { "version": "1.1.0", "isPreRelease": false, "publishedUtc": "2026-09-10T10:00:00Z", "markdown": "## 1.1.0\nNicer." }
          ],
          "markdown": "## 1.2.0\nFaster.\n\n## 1.1.0\nNicer.",
          "truncated": false
        }
        """;

    [Fact]
    public async Task No_start_version_means_no_call_at_all()
    {
        // The rule the whole read path is built on: a first-ever visitor has nothing to catch up on,
        // so the consumer does not call — and if it calls anyway, this does not turn into "send the
        // entire history". Short-circuited here so the common case costs nothing.
        using var listener = StartListener(out var prefix, out var state, TwoNotes);
        var sut = Build(prefix);

        var result = await sut.GetDeltaAsync(null, cancellationToken: TestContext.Current.CancellationToken);

        result.HasNews.Should().BeFalse();
        state.Requests.Should().BeEmpty("no version to start from is not a request the server can answer");
    }

    [Fact]
    public async Task A_version_that_is_not_semver_is_refused_loudly_rather_than_looking_like_good_news()
    {
        // 'v1.2.3' and '1.2.3.4' are the two spellings a pipeline reaches for by accident. Returning
        // an empty delta for them is indistinguishable from "you are up to date", so it is logged as
        // a warning naming the value.
        using var listener = StartListener(out var prefix, out var state, TwoNotes);
        var logs = new CapturingLoggerProvider();
        var sut = Build(prefix, logs);

        var result = await sut.GetDeltaAsync("v1.2.3", cancellationToken: TestContext.Current.CancellationToken);

        result.HasNews.Should().BeFalse();
        state.Requests.Should().BeEmpty();
        logs.Entries.Should().Contain(x => x.Contains("v1.2.3") && x.Contains("SemVer"));
    }

    [Fact]
    public async Task The_delta_carries_the_version_the_application_and_the_prerelease_choice()
    {
        using var listener = StartListener(out var prefix, out var state, TwoNotes);
        var sut = Build(prefix);

        await sut.GetDeltaAsync("1.0.0", includePreRelease: true, application: "App1", cancellationToken: TestContext.Current.CancellationToken);

        var request = state.Requests.Should().ContainSingle().Subject;
        request.Should().StartWith("/Api/ReleaseNote/delta?");
        request.Should().Contain("sinceVersion=1.0.0").And.Contain("application=App1").And.Contain("includePreRelease=true");
    }

    [Fact]
    public async Task Pre_releases_are_left_out_unless_asked_for()
    {
        using var listener = StartListener(out var prefix, out var state, TwoNotes);
        var sut = Build(prefix);

        await sut.GetDeltaAsync("1.0.0", cancellationToken: TestContext.Current.CancellationToken);

        state.Requests.Single().Should().Contain("includePreRelease=false", "most end users should not be told about a beta");
    }

    [Fact]
    public async Task The_delta_comes_back_newest_first_with_the_version_to_remember()
    {
        using var listener = StartListener(out var prefix, out _, TwoNotes);
        var sut = Build(prefix);

        var result = await sut.GetDeltaAsync("1.0.0", cancellationToken: TestContext.Current.CancellationToken);

        result.HasNews.Should().BeTrue();
        result.Notes.Select(x => x.Version).Should().Equal("1.2.0", "1.1.0");
        result.Markdown.Should().Contain("Faster.").And.Contain("Nicer.");
        // What the consumer stores as the new mark once it has shown the news.
        result.LatestVersion.Should().Be("1.2.0");
    }

    [Fact]
    public async Task A_server_that_cannot_answer_costs_the_page_nothing()
    {
        // Release notes are decoration. A consumer rendering a page should never have to catch an
        // exception because the news service is unhappy.
        using var listener = StartListener(out var prefix, out _, "nope", status: 500);
        var sut = Build(prefix);

        var result = await sut.GetDeltaAsync("1.0.0", cancellationToken: TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.HasNews.Should().BeFalse();
    }

    [Fact]
    public async Task A_version_with_no_note_published_is_null_rather_than_an_error()
    {
        using var listener = StartListener(out var prefix, out _, "", status: 404);
        var sut = Build(prefix);

        var result = await sut.GetAsync("1.4.0", cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task One_version_is_fetched_by_name()
    {
        using var listener = StartListener(out var prefix, out var state,
            """{ "version": "1.4.0", "isPreRelease": false, "publishedUtc": "2026-09-20T10:00:00Z", "markdown": "# 1.4.0" }""");
        var sut = Build(prefix);

        var result = await sut.GetAsync("1.4.0", application: "App1", cancellationToken: TestContext.Current.CancellationToken);

        result.Markdown.Should().Be("# 1.4.0");
        state.Requests.Single().Should().StartWith("/Api/ReleaseNote?").And.Contain("version=1.4.0");
    }

    [Fact]
    public async Task Without_an_api_key_nothing_is_requested()
    {
        using var listener = StartListener(out var prefix, out var state, TwoNotes);
        var sut = Build(prefix, apiKey: null);

        var result = await sut.GetDeltaAsync("1.0.0", cancellationToken: TestContext.Current.CancellationToken);

        result.HasNews.Should().BeFalse();
        state.Requests.Should().BeEmpty("no key means the call cannot succeed, so it is not made");
    }

    private static IReleaseNoteService Build(string baseAddress, ILoggerProvider loggerProvider = null, string apiKey = "test-key")
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                if (loggerProvider != null) services.AddLogging(b => b.AddProvider(loggerProvider).SetMinimumLevel(LogLevel.Debug));
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = apiKey;
                    o.Application = "App1";
                    o.WarmUpEnabled = false;
                });
            })
            .Build();

        return host.Services.GetRequiredService<IReleaseNoteService>();
    }

    private sealed class ListenerState
    {
        /// <summary>Path + query of every request that reached the listener — these are GETs, so the
        /// query string is the whole payload.</summary>
        public ConcurrentBag<string> Requests { get; } = [];
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

    private static HttpListener StartListener(out string prefix, out ListenerState state, string responseBody, int status = 200)
    {
        var port = GetFreePort();
        prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var s = new ListenerState();
        state = s;

        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { return; }

                s.Requests.Add(ctx.Request.Url?.PathAndQuery ?? "");

                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json";
                var buf = System.Text.Encoding.UTF8.GetBytes(responseBody);
                try
                {
                    await ctx.Response.OutputStream.WriteAsync(buf);
                    ctx.Response.Close();
                }
                catch { }
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
