using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Quilt4Net.Toolkit;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Repro for https://github.com/Quilt4/Quilt4Net.Toolkit/issues/87 — when a consumer
/// calls <c>AddQuilt4NetLogging()</c> BEFORE registering another ILoggerProvider
/// (e.g. Microsoft.ApplicationInsights.AspNetCore) AND replaces ILoggerFactory with
/// a custom wrap that iterates <c>sp.GetServices&lt;ILoggerProvider&gt;()</c>, the
/// AppTraces flow silently breaks — log records never reach the OTel processor that
/// would forward them to the Azure Monitor exporter. The reverse order works.
///
/// These tests pin the symptom and let us validate any fix.
/// </summary>
public class RegistrationOrderTests
{
    [Fact]
    public void Quilt4Net_first_then_other_provider_then_factory_wrap_REACHES_otel_processor()
    {
        var capturedByOTel = new List<string>();
        var capturedByOther = new List<string>();

        var services = new ServiceCollection();

        // (1) Quilt4Net first — registers OTel pipeline via AddOpenTelemetry().WithLogging(...).
        services.AddOpenTelemetry()
            .WithLogging(b => b.AddProcessor(new CaptureProcessor(r => capturedByOTel.Add(r.Body!))));

        // (2) Some other ILoggerProvider — stand-in for Microsoft.ApplicationInsights.AspNetCore's
        //     ApplicationInsightsLoggerProvider. We register it the same way AI does:
        //     services.AddSingleton<ILoggerProvider, ApplicationInsightsLoggerProvider>().
        services.AddSingleton<ILoggerProvider>(_ => new CapturingLoggerProvider(capturedByOther));

        // (3) Custom ILoggerFactory wrap — exactly the shape the consumer described:
        //     "iterates sp.GetServices<ILoggerProvider>() and rebuilds a LoggerFactory".
        services.AddSingleton<ILoggerFactory>(sp =>
        {
            var providers = sp.GetServices<ILoggerProvider>().ToArray();
            return new LoggerFactory(providers);
        });

        using var sp2 = services.BuildServiceProvider();
        var loggerFactory = sp2.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("test");
        logger.LogInformation("hello-broken-order");

        // Force the OTel SDK to flush any batched log records before assertion.
        sp2.GetRequiredService<LoggerProvider>().ForceFlush(2_000);

        capturedByOther.Should().Contain("hello-broken-order",
            "the AI-style ILoggerProvider must always see the record (sanity check)");

        capturedByOTel.Should().Contain("hello-broken-order",
            "ISSUE #87: the OTel processor should also see the record, regardless of registration order");
    }

    [Fact]
    public void Other_provider_first_then_factory_wrap_then_Quilt4Net_REACHES_otel_processor()
    {
        var capturedByOTel = new List<string>();
        var capturedByOther = new List<string>();

        var services = new ServiceCollection();

        // (1) Other provider first.
        services.AddSingleton<ILoggerProvider>(_ => new CapturingLoggerProvider(capturedByOther));

        // (2) Custom ILoggerFactory wrap — at this point only the other provider exists in DI.
        services.AddSingleton<ILoggerFactory>(sp =>
        {
            var providers = sp.GetServices<ILoggerProvider>().ToArray();
            return new LoggerFactory(providers);
        });

        // (3) Quilt4Net last.
        services.AddOpenTelemetry()
            .WithLogging(b => b.AddProcessor(new CaptureProcessor(r => capturedByOTel.Add(r.Body!))));

        using var sp2 = services.BuildServiceProvider();
        var loggerFactory = sp2.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("test");
        logger.LogInformation("hello-working-order");

        sp2.GetRequiredService<LoggerProvider>().ForceFlush(2_000);

        capturedByOther.Should().Contain("hello-working-order");
        capturedByOTel.Should().Contain("hello-working-order");
    }

    [Fact]
    public void Quilt4Net_first_then_other_provider_via_AddLogging_then_factory_wrap_REACHES_otel_processor()
    {
        // Variant: the "other provider" is registered via `services.AddLogging(b => b.AddProvider(...))`
        // (which is the canonical pattern — Microsoft.ApplicationInsights.AspNetCore registers its
        // ApplicationInsightsLoggerProvider through ILoggingBuilder, not directly via TryAddEnumerable).
        var capturedByOTel = new List<string>();
        var capturedByOther = new List<string>();

        var services = new ServiceCollection();

        services.AddOpenTelemetry()
            .WithLogging(b => b.AddProcessor(new CaptureProcessor(r => capturedByOTel.Add(r.Body!))));

        services.AddLogging(b => b.AddProvider(new CapturingLoggerProvider(capturedByOther)));

        services.AddSingleton<ILoggerFactory>(sp =>
        {
            var providers = sp.GetServices<ILoggerProvider>().ToArray();
            return new LoggerFactory(providers);
        });

        using var sp2 = services.BuildServiceProvider();
        var loggerFactory = sp2.GetRequiredService<ILoggerFactory>();
        loggerFactory.CreateLogger("test").LogInformation("hello-via-addlogging");
        sp2.GetRequiredService<LoggerProvider>().ForceFlush(2_000);

        capturedByOther.Should().Contain("hello-via-addlogging");
        capturedByOTel.Should().Contain("hello-via-addlogging");
    }

    [Fact]
    public void Quilt4Net_first_then_factory_replace_via_Replace_loses_otel_processor_or_not()
    {
        // Variant: the consumer replaces ILoggerFactory via `services.Replace(...)` rather than
        // adding a new singleton on top. This semantically removes the previous registration.
        // Tests whether OTel's auto-injected ILoggerFactory binding survives a Replace.
        var capturedByOTel = new List<string>();

        var services = new ServiceCollection();

        services.AddOpenTelemetry()
            .WithLogging(b => b.AddProcessor(new CaptureProcessor(r => capturedByOTel.Add(r.Body!))));

        // Now Replace the registered ILoggerFactory with a wrap (Replace removes existing,
        // adds new). Mimics a stronger "I own ILoggerFactory" pattern.
        services.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(sp =>
        {
            var providers = sp.GetServices<ILoggerProvider>().ToArray();
            return new LoggerFactory(providers);
        }));

        using var sp2 = services.BuildServiceProvider();
        var loggerFactory = sp2.GetRequiredService<ILoggerFactory>();
        loggerFactory.CreateLogger("test").LogInformation("hello-replaced");
        sp2.GetRequiredService<LoggerProvider>().ForceFlush(2_000);

        capturedByOTel.Should().Contain("hello-replaced");
    }

    private sealed class CaptureProcessor : BaseProcessor<LogRecord>
    {
        private readonly Action<LogRecord> _capture;
        public CaptureProcessor(Action<LogRecord> capture) => _capture = capture;
        public override void OnEnd(LogRecord data) => _capture(data);
    }

    /// <summary>
    /// Minimal stand-in for <c>Microsoft.ApplicationInsights.AspNetCore</c>'s
    /// <c>ApplicationInsightsLoggerProvider</c> — captures log message bodies so the
    /// test can assert which providers actually saw a given record.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _captured;
        public CapturingLoggerProvider(List<string> captured) => _captured = captured;
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_captured);
        public void Dispose() { }

        private sealed class CapturingLogger : ILogger
        {
            private readonly List<string> _captured;
            public CapturingLogger(List<string> captured) => _captured = captured;
            public IDisposable BeginScope<TState>(TState state) => null!;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
                => _captured.Add(formatter(state, exception));
        }
    }
}
