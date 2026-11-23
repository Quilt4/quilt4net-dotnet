using System.Collections.Concurrent;
using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

/// <summary>
/// The consumer half of the content write API (#161) — the client a host calls from a maintenance
/// action or a CI step to change text a key already holds.
/// </summary>
namespace Quilt4Net.Toolkit.Tests;

public class ContentWriteServiceTests
{
    [Fact]
    public async Task Import_posts_the_items_and_returns_what_the_server_did()
    {
        using var listener = StartListener(out var prefix, out var state, responseBody:
            """{"keysWritten":2,"valuesWritten":3,"ignoredLanguages":[]}""");
        var sut = Build(prefix);

        var result = await sut.ImportAsync([Item("a", "Add"), Item("b", "Remove")], TestContext.Current.CancellationToken);

        result.KeysWritten.Should().Be(2);
        result.ValuesWritten.Should().Be(3);
        state.Requests.Should().ContainSingle().Which.Should().Contain("\"key\":\"a\"").And.Contain("\"key\":\"b\"");
    }

    [Fact]
    public async Task Import_carries_the_runtime_environment_so_the_server_can_refuse_a_wrong_one()
    {
        // The server refuses a write whose environment resolves above the lowest stage. That guard
        // only works if the client actually tells it which environment it thinks it is.
        using var listener = StartListener(out var prefix, out var state, responseBody:
            """{"keysWritten":1,"valuesWritten":1,"ignoredLanguages":[]}""");
        var sut = Build(prefix);

        await sut.ImportAsync([Item("a", "Add")], TestContext.Current.CancellationToken);

        state.Requests.Single().Should().Contain("\"environment\":");
    }

    [Fact]
    public async Task Import_warns_about_a_language_name_the_server_did_not_recognise()
    {
        // This is the failure that otherwise looks exactly like success: 200 OK, and the Swedish
        // text simply never appears because the payload said "Svenska" and the server says
        // "Swedish". A silent skip here is how a wording fix gets marked done and is not done.
        using var listener = StartListener(out var prefix, out _, responseBody:
            """{"keysWritten":1,"valuesWritten":1,"ignoredLanguages":["Svenska"]}""");
        var logs = new CapturingLoggerProvider();
        var sut = Build(prefix, logs);

        var result = await sut.ImportAsync([Item("a", "Add")], TestContext.Current.CancellationToken);

        result.IgnoredLanguages.Should().ContainSingle().Which.Should().Be("Svenska");
        logs.Entries.Should().Contain(x => x.Contains("Svenska") && x.Contains("not written"));
    }

    [Fact]
    public async Task A_failed_import_throws_rather_than_reporting_an_empty_success()
    {
        // Every read path in this client degrades quietly, because stale text beats a broken page.
        // A write is the opposite: reporting success while changing nothing means the caller records
        // the change as done and nobody looks again.
        using var listener = StartListener(out var prefix, out _, responseBody: "nope", status: 403);
        var sut = Build(prefix);

        var act = async () => await sut.ImportAsync([Item("a", "Add")], TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*403*");
    }

    [Fact]
    public async Task Import_without_an_api_key_says_which_scope_is_needed()
    {
        using var listener = StartListener(out var prefix, out _, responseBody: "{}");
        var sut = Build(prefix, apiKey: null);

        var act = async () => await sut.ImportAsync([Item("a", "Add")], TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*content:write*");
    }

    [Fact]
    public async Task Import_of_nothing_calls_nothing()
    {
        using var listener = StartListener(out var prefix, out var state, responseBody: "{}");
        var sut = Build(prefix);

        var result = await sut.ImportAsync([], TestContext.Current.CancellationToken);

        result.KeysWritten.Should().Be(0);
        state.Requests.Should().BeEmpty("an empty write is a no-op, not a round trip");
    }

    private static ContentImportItem Item(string key, string value)
        => new() { Key = key, Application = "App1", Instance = string.Empty, DefaultValue = value };

    private static IContentWriteService Build(string baseAddress, ILoggerProvider loggerProvider = null, string apiKey = "test-key")
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                if (loggerProvider != null) services.AddLogging(b => b.AddProvider(loggerProvider).SetMinimumLevel(LogLevel.Debug));
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = apiKey;
                    o.WarmUpEnabled = false;
                });
            })
            .Build();

        return host.Services.GetRequiredService<IContentWriteService>();
    }

    private sealed class ListenerState
    {
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

                using (var reader = new StreamReader(ctx.Request.InputStream))
                {
                    s.Requests.Add(await reader.ReadToEndAsync());
                }

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
