using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Quilt4Net.Toolkit.Features.Logging;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class Quilt4NetStartupLoggerTests
{
    [Fact]
    public void Log_emits_information_entry_with_application_details()
    {
        var capture = new CapturingLogger();
        var options = new Quilt4NetLoggingOptions
        {
            ApplicationName = "florida-server",
            Version = "1.4.7",
            Environment = "Production"
        };

        Quilt4NetStartupLogger.Log(capture, options);

        capture.Entries.Should().ContainSingle();
        var entry = capture.Entries[0];
        entry.Level.Should().Be(LogLevel.Information);
        entry.FormattedMessage.Should().Contain("florida-server");
        entry.FormattedMessage.Should().Contain("1.4.7");
        entry.FormattedMessage.Should().Contain("Production");
    }

    [Fact]
    public void Log_emits_Quilt4NetStartup_as_a_structured_property_not_a_scope()
    {
        // Regression guard for the previous BeginScope-based implementation: OpenTelemetry's
        // Microsoft.Extensions.Logging bridge does NOT copy scope state into LogRecord.Attributes
        // unless the consumer sets OpenTelemetryLoggerOptions.IncludeScopes = true. The Azure
        // Monitor exporter (the toolkit's recommended ingestion path) defaults that to false, so
        // a scope-based emission of "Quilt4NetStartup=true" never reached App Insights — making
        // the VersionMatrix "Startup" fast path unreachable for every consumer. Pin the new
        // contract: the property goes into the message template's structured state, not a scope.
        var capture = new CapturingLogger();
        var options = new Quilt4NetLoggingOptions { ApplicationName = "x" };

        Quilt4NetStartupLogger.Log(capture, options);

        capture.Entries.Should().ContainSingle();
        capture.Entries[0].State.Should().Contain(kv => kv.Key == "Quilt4NetStartup" && kv.Value!.Equals("true"));
        capture.Entries[0].Scopes.Should().BeEmpty("Quilt4NetStartup must NOT be emitted via BeginScope");
    }

    [Fact]
    public void Log_emits_Quilt4NetStartup_into_OTel_LogRecord_Attributes_without_IncludeScopes()
    {
        // End-to-end regression guard for the actual reported bug: build a real OTel pipeline
        // with IncludeScopes left at its `false` default, route ILogger through it, and assert
        // the captured LogRecord.Attributes contains ("Quilt4NetStartup", "true"). If anyone
        // ever reverts to scope-based emission, this test fails immediately — proving the
        // property would have been dropped before reaching the Azure Monitor exporter.
        // The attributes are copied inside the pipeline: from OpenTelemetry 1.20 a LogRecord is returned to a
        // pool once the pipeline is done, so a record read afterwards has lost its attributes.
        List<KeyValuePair<string, object>> attributes = null;
        using var loggerFactory = LoggerFactory.Create(b =>
        {
            b.AddOpenTelemetry(o => o.AddProcessor(new CaptureLogRecordProcessor(r => attributes = r.Attributes?.ToList())));
        });
        var logger = loggerFactory.CreateLogger<Quilt4NetStartupLoggerTests>();
        var options = new Quilt4NetLoggingOptions
        {
            ApplicationName = "florida-client",
            Version = "1.4.7",
            Environment = "Development"
        };

        Quilt4NetStartupLogger.Log(logger, options);

        attributes.Should().NotBeNull();
        attributes.Should().Contain(kv => kv.Key == "Quilt4NetStartup" && kv.Value!.Equals("true"));
    }

    [Fact]
    public void Log_handles_null_option_values_with_unknown_placeholder()
    {
        var capture = new CapturingLogger();
        var options = new Quilt4NetLoggingOptions();

        Quilt4NetStartupLogger.Log(capture, options);

        capture.Entries[0].FormattedMessage.Should().Contain("(unknown)");
    }

    [Fact]
    public void Log_includes_service_instance_id_in_brackets_when_set()
    {
        // Issue #86: when ServiceInstanceId is configured, the startup line surfaces it
        // between the application name and the version so multi-deployment hosts are
        // distinguishable at-a-glance, e.g. "Eplicta.FortDocs.Server [Thargelion] v1.2.9 ..."
        var capture = new CapturingLogger();
        var options = new Quilt4NetLoggingOptions
        {
            ApplicationName = "Eplicta.FortDocs.Server",
            ServiceInstanceId = "Thargelion",
            Version = "1.2.9.0",
            Environment = "CI"
        };

        Quilt4NetStartupLogger.Log(capture, options);

        // MachineName is sourced from System.Environment.MachineName at runtime, so the value
        // varies per host — match the surrounding shape with a wildcard rather than pinning the
        // full string. The structured "MachineName" property's value is asserted separately
        // (see Log_emits_MachineName_as_a_structured_property).
        capture.Entries[0].FormattedMessage
            .Should().Match("Quilt4Net startup: Eplicta.FortDocs.Server [Thargelion] v1.2.9.0 in CI on * (true)");
    }

    [Fact]
    public void Log_omits_brackets_when_service_instance_id_is_null_for_back_compat()
    {
        // The historical message shape must be preserved when the new option is unset, so
        // existing log scrapers / dashboards that match the old format don't regress.
        var capture = new CapturingLogger();
        var options = new Quilt4NetLoggingOptions
        {
            ApplicationName = "Eplicta.FortDocs.Server",
            ServiceInstanceId = null,
            Version = "1.2.9.0",
            Environment = "CI"
        };

        Quilt4NetStartupLogger.Log(capture, options);

        capture.Entries[0].FormattedMessage
            .Should().Match("Quilt4Net startup: Eplicta.FortDocs.Server v1.2.9.0 in CI on * (true)")
            .And.NotContain("[");
    }

    [Fact]
    public void Log_emits_MachineName_as_a_structured_property()
    {
        // Companion to Log_emits_Quilt4NetStartup_as_a_structured_property_not_a_scope. Pins
        // that MachineName is delivered as a message-template structured arg (not BeginScope)
        // so it reaches App Insights' Properties bag on the Quilt4NetStartup-tagged row — the
        // VersionMatrix per-machine view relies on this to label startup rows with a real host
        // name. Value matches System.Environment.MachineName at the call site.
        var capture = new CapturingLogger();
        var options = new Quilt4NetLoggingOptions { ApplicationName = "x" };

        Quilt4NetStartupLogger.Log(capture, options);

        capture.Entries.Should().ContainSingle();
        capture.Entries[0].State
            .Should().Contain(kv => kv.Key == "MachineName"
                && string.Equals(kv.Value as string, System.Environment.MachineName, System.StringComparison.Ordinal));
    }

    [Fact]
    public async Task Hosted_service_emits_log_on_StartAsync()
    {
        var capture = new CapturingLogger<Quilt4NetStartupHostedService>();
        var options = new Quilt4NetLoggingOptions { ApplicationName = "from-host" };
        var service = new Quilt4NetStartupHostedService(capture, options);

        await service.StartAsync(CancellationToken.None);

        capture.Inner.Entries.Should().ContainSingle()
            .Which.FormattedMessage.Should().Contain("from-host");
    }

    [Fact]
    public void LogQuilt4NetStartup_extension_resolves_logger_from_provider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Quilt4NetLoggingOptions { ApplicationName = "wpf-app", Version = "2.0.0", Environment = "Production" });
        var capture = new CapturingLogger();
        services.AddSingleton<ILoggerFactory>(new CapturingLoggerFactory(capture));
        var provider = services.BuildServiceProvider();

        provider.LogQuilt4NetStartup();

        capture.Entries.Should().ContainSingle()
            .Which.FormattedMessage.Should().Contain("wpf-app");
    }

    [Fact]
    public void Hosted_service_is_registered_by_AddQuilt4NetLogging_via_IServiceCollection_overload()
    {
        var services = new ServiceCollection();
        services.AddQuilt4NetLogging(applicationName: "x");

        services.Should().Contain(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(Quilt4NetStartupHostedService));
    }

    [Fact]
    public void Hosted_service_is_registered_by_AddQuilt4NetLogging_via_IHostApplicationBuilder_overload()
    {
        // The IHostApplicationBuilder overload delegates to the IServiceCollection overload —
        // pinning that hosted-service registration survives the indirection so legacy
        // IHostBuilder / Tharga.Wpf consumers that route through the IHostApplicationBuilder
        // overload also get the startup entry without calling LogQuilt4NetStartup manually.
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddQuilt4NetLogging();

        builder.Services.Should().Contain(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(Quilt4NetStartupHostedService));
    }

    private sealed record LogEntry(LogLevel Level, string FormattedMessage, List<object> Scopes, IReadOnlyList<KeyValuePair<string, object>> State);

    private sealed class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = new();
        private readonly List<object> _activeScopes = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            _activeScopes.Add(state);
            return new Pop(() => _activeScopes.RemoveAt(_activeScopes.Count - 1));
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            // ILogger's structured state is conventionally an IReadOnlyList<KeyValuePair<string, object>>
            // when callers use the message-template overloads (LogInformation("{Foo}", value)). Capturing
            // it lets the tests assert on per-property structured data without spinning up an OTel
            // pipeline for every assertion.
            var stateList = state as IReadOnlyList<KeyValuePair<string, object>>
                ?? new List<KeyValuePair<string, object>>();
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), new List<object>(_activeScopes), stateList));
        }

        private sealed class Pop : IDisposable
        {
            private readonly Action _action;
            public Pop(Action action) { _action = action; }
            public void Dispose() => _action();
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public CapturingLogger Inner { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => Inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => Inner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => Inner.Log(logLevel, eventId, state, exception, formatter);
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly CapturingLogger _logger;
        public CapturingLoggerFactory(CapturingLogger logger) { _logger = logger; }
        public ILogger CreateLogger(string categoryName) => _logger;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
    }

    private sealed class CaptureLogRecordProcessor : BaseProcessor<LogRecord>
    {
        private readonly Action<LogRecord> _capture;
        public CaptureLogRecordProcessor(Action<LogRecord> capture) => _capture = capture;
        public override void OnEnd(LogRecord data) => _capture(data);
    }
}
