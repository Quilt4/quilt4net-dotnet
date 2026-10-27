using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// A team can switch its Remote configuration off on the server, which then answers every read with
/// a 403 naming the feature. The application keeps running on its defaults - that is the design - so
/// the refusal is a warning carrying the server's sentence, not an error on every read.
/// </summary>
public class ConfigurationSwitchedOffTests
{
    private const string SwitchedOff = """{"title":"Forbidden","status":403,"detail":"'config:read' belongs to Remote configuration, which is switched off for this team."}""";

    [Fact]
    public async Task A_403_serves_the_fallback_and_warns_with_the_servers_sentence()
    {
        using var listener = StartListener(HttpStatusCode.Forbidden, SwitchedOff, out var prefix);
        var log = new List<(LogLevel Level, string Message)>();
        var service = BuildToggleService(prefix, log);

        var result = await service.GetToggleResultAsync("AssistantPanel.Enabled", fallback: true);

        result.Value.Should().BeTrue("the caller's fallback stands");
        result.Source.Should().Be(ConfigurationSource.Fallback);
        log.Should().NotContain(x => x.Level >= LogLevel.Error);
        log.Should().Contain(x => x.Level == LogLevel.Warning && x.Message.Contains("'config:read' belongs to Remote configuration, which is switched off for this team."));
    }

    [Fact]
    public async Task Any_other_failure_is_still_an_error()
    {
        using var listener = StartListener(HttpStatusCode.InternalServerError, null, out var prefix);
        var log = new List<(LogLevel Level, string Message)>();
        var service = BuildToggleService(prefix, log);

        await service.GetToggleResultAsync("AssistantPanel.Enabled", fallback: true);

        log.Should().Contain(x => x.Level == LogLevel.Error);
    }

    private static IFeatureToggleService BuildToggleService(string baseAddress, List<(LogLevel, string)> log)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(b => b.ClearProviders().AddProvider(new RecordingProvider(log)))
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetRemoteConfiguration(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                });
            })
            .Build();

        return host.Services.GetRequiredService<IFeatureToggleService>();
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
                if (body != null)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(body);
                    ctx.Response.ContentType = "application/problem+json";
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                }
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
