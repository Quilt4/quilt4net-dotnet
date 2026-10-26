using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// A team can switch its Content off on the server, which then answers every read with a 403 naming the
/// feature. Pages keep rendering their defaults - that is the design - so the refusal is one warning
/// carrying the server's sentence, not an error per key.
/// </summary>
public class ContentSwitchedOffTests
{
    private const string SwitchedOff = """{"title":"Forbidden","status":403,"detail":"'content:read' belongs to Content, which is switched off for this team."}""";

    [Fact]
    public async Task A_403_serves_the_defaults_and_warns_once_with_the_servers_sentence()
    {
        using var listener = StartListener(HttpStatusCode.Forbidden, SwitchedOff, out var prefix);
        var log = new List<(LogLevel Level, string Message)>();
        var service = BuildContentService(prefix, log);

        var first = await service.GetContentAsync("Page.Title", "Welcome", Guid.Empty, ContentFormat.String, application: "App");
        var second = await service.GetContentAsync("Page.Intro", "Hello", Guid.Empty, ContentFormat.String, application: "App");
        var third = await service.GetContentAsync("Page.Footer", "Bye", Guid.Empty, ContentFormat.String, application: "App");

        new[] { first.Value, second.Value, third.Value }.Should().Equal("Welcome", "Hello", "Bye");
        lock (log)
        {
            log.Should().NotContain(x => x.Level >= LogLevel.Error);
            log.Where(x => x.Level == LogLevel.Warning && x.Message.Contains("'content:read' belongs to Content, which is switched off for this team."))
                .Should().ContainSingle("a page of many keys logs the refusal once, not per key");
        }
    }

    private static IContentService BuildContentService(string baseAddress, List<(LogLevel, string)> log)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    o.SlowLogThreshold = TimeSpan.Zero;
                    o.FailureCacheDuration = TimeSpan.FromMinutes(5);
                });
                services.AddLogging(b => b.ClearProviders().AddProvider(new RecordingProvider(log)));
            })
            .Build();

        return host.Services.GetRequiredService<IContentService>();
    }

    private static HttpListener StartListener(HttpStatusCode status, string body, out string prefix)
    {
        using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            prefix = $"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}/";
            probe.Stop();
        }

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

                ctx.Response.StatusCode = (int)status;
                var bytes = System.Text.Encoding.UTF8.GetBytes(body);
                ctx.Response.ContentType = "application/problem+json";
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });

        return listener;
    }

    private sealed class RecordingProvider(List<(LogLevel, string)> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Recorder(entries);
        public void Dispose() { }

        private sealed class Recorder(List<(LogLevel, string)> entries) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                lock (entries) entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
