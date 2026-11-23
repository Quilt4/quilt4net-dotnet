using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Quilt4Net.Toolkit;
using Quilt4Net.Toolkit.Features.Logging;
using Quilt4Net.Toolkit.Features.Measure;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class ExceptionDataEnrichmentTests
{
    [Fact]
    public void EnrichExceptionData_defaults_to_on()
    {
        new Quilt4NetLoggingOptions().EnrichExceptionData.Should().BeTrue();
    }

    [Fact]
    public void Processor_copies_exception_data_onto_record_attributes()
    {
        var correlationId = "1f56e7db-7f58-4c72-8a74-af7a896e750d";
        var exception = new InvalidOperationException("boom").AddData("CorrelationId", correlationId);

        var attributes = Enrich(exception);

        attributes.Should().Contain(new KeyValuePair<string, object>("CorrelationId", correlationId));
    }

    [Fact]
    public void Processor_copies_multiple_entries()
    {
        var exception = new Exception("boom")
            .AddData("CorrelationId", "abc")
            .AddData("TenantId", "eplicta");

        var attributes = Enrich(exception);

        attributes.Should().Contain(kv => kv.Key == "CorrelationId" && kv.Value!.Equals("abc"));
        attributes.Should().Contain(kv => kv.Key == "TenantId" && kv.Value!.Equals("eplicta"));
    }

    [Fact]
    public void Processor_stringifies_values_with_invariant_culture()
    {
        var exception = new Exception("boom").AddData("Ratio", 1.5d);

        var attributes = Enrich(exception);

        attributes.Should().Contain(new KeyValuePair<string, object>("Ratio", "1.5"));
    }

    [Fact]
    public void Processor_skips_null_values()
    {
        var exception = new Exception("boom");
        exception.Data["Missing"] = null;

        var attributes = Enrich(exception);

        attributes.Should().NotContain(kv => kv.Key == "Missing");
    }

    [Fact]
    public void Processor_does_not_overwrite_an_existing_attribute_with_the_same_key()
    {
        var exception = new Exception("boom").AddData("CorrelationId", "from-exception");
        var seed = new List<KeyValuePair<string, object>> { new("CorrelationId", "already-present") };

        var attributes = Enrich(exception, seed);

        attributes.Should().ContainSingle(kv => kv.Key == "CorrelationId")
            .Which.Value.Should().Be("already-present");
    }

    [Fact]
    public void Processor_is_a_noop_when_the_record_has_no_exception()
    {
        List<KeyValuePair<string, object>> captured = null;
        using var loggerFactory = LoggerFactory.Create(b => b.AddOpenTelemetry(o =>
        {
            o.AddProcessor(new ExceptionDataLogProcessor());
            o.AddProcessor(new CaptureProcessor(r => captured = r.Attributes?.ToList() ?? new List<KeyValuePair<string, object>>()));
        }));

        loggerFactory.CreateLogger("test").LogInformation("no exception here");

        captured.Should().NotContain(kv => kv.Key == "CorrelationId");
    }

    [Fact]
    public void AddQuilt4NetLogging_wires_the_enricher_by_default()
    {
        var attributes = EnrichViaRegistration(enrich: null, new Exception("boom").AddData("CorrelationId", "wired"));

        attributes.Should().Contain(kv => kv.Key == "CorrelationId" && kv.Value!.Equals("wired"));
    }

    [Fact]
    public void AddQuilt4NetLogging_does_not_wire_the_enricher_when_disabled()
    {
        var attributes = EnrichViaRegistration(enrich: false, new Exception("boom").AddData("CorrelationId", "should-be-absent"));

        attributes.Should().NotContain(kv => kv.Key == "CorrelationId");
    }

    [Fact]
    public void IncludeScopes_defaults_to_off()
    {
        new Quilt4NetLoggingOptions().IncludeScopes.Should().BeFalse();
    }

    [Fact]
    public void ScopeProcessor_copies_dictionary_scope_values_onto_record_attributes()
    {
        var attributes = EnrichScopes(
            logger => logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = "scope-guid" }),
            log: logger => logger.LogInformation("hi"));

        attributes.Should().Contain(kv => kv.Key == "CorrelationId" && kv.Value!.Equals("scope-guid"));
    }

    [Fact]
    public void ScopeProcessor_captures_named_values_from_a_message_template_scope()
    {
        var attributes = EnrichScopes(
            logger => logger.BeginScope("Order {OrderId}", 7),
            log: logger => logger.LogInformation("hi"));

        attributes.Should().Contain(kv => kv.Key == "OrderId" && kv.Value!.Equals("7"));
    }

    [Fact]
    public void AddQuilt4NetLogging_wires_scope_capture_when_IncludeScopes_is_true()
    {
        var attributes = ScopesViaRegistration(includeScopes: true);

        attributes.Should().Contain(kv => kv.Key == "CorrelationId" && kv.Value!.Equals("wired-scope"));
    }

    [Fact]
    public void AddQuilt4NetLogging_does_not_wire_scope_capture_by_default()
    {
        var attributes = ScopesViaRegistration(includeScopes: null);

        attributes.Should().NotContain(kv => kv.Key == "CorrelationId");
    }

    private static List<KeyValuePair<string, object>> EnrichScopes(System.Func<ILogger, System.IDisposable> beginScope, System.Action<ILogger> log)
    {
        List<KeyValuePair<string, object>> captured = null;
        using var loggerFactory = LoggerFactory.Create(b => b.AddOpenTelemetry(o =>
        {
            o.IncludeScopes = true;
            o.AddProcessor(new ScopeAttributesLogProcessor());
            o.AddProcessor(new CaptureProcessor(r => captured = r.Attributes?.ToList() ?? new List<KeyValuePair<string, object>>()));
        }));

        var logger = loggerFactory.CreateLogger("test");
        using (beginScope(logger)) log(logger);
        return captured;
    }

    private static List<KeyValuePair<string, object>> ScopesViaRegistration(bool? includeScopes)
    {
        List<KeyValuePair<string, object>> captured = null;
        var services = new ServiceCollection();
        services.AddQuilt4NetLogging(options: o =>
        {
            o.ApplicationName = "test-app";
            if (includeScopes.HasValue) o.IncludeScopes = includeScopes.Value;
        });
        services.AddOpenTelemetry().WithLogging(b =>
            b.AddProcessor(new CaptureProcessor(r => captured = r.Attributes?.ToList() ?? new List<KeyValuePair<string, object>>())));

        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("test");
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = "wired-scope" }))
        {
            logger.LogInformation("hi");
        }
        return captured;
    }

    private static List<KeyValuePair<string, object>> Enrich(System.Exception exception, IList<KeyValuePair<string, object>> seedAttributes = null)
    {
        List<KeyValuePair<string, object>> captured = null;
        using var loggerFactory = LoggerFactory.Create(b => b.AddOpenTelemetry(o =>
        {
            if (seedAttributes != null) o.AddProcessor(new SeedProcessor(seedAttributes));
            o.AddProcessor(new ExceptionDataLogProcessor());
            o.AddProcessor(new CaptureProcessor(r => captured = r.Attributes?.ToList() ?? new List<KeyValuePair<string, object>>()));
        }));

        loggerFactory.CreateLogger("test").LogError(exception, "boom");
        return captured;
    }

    private static List<KeyValuePair<string, object>> EnrichViaRegistration(bool? enrich, System.Exception exception)
    {
        List<KeyValuePair<string, object>> captured = null;
        var services = new ServiceCollection();
        services.AddQuilt4NetLogging(options: o =>
        {
            o.ApplicationName = "test-app";
            if (enrich.HasValue) o.EnrichExceptionData = enrich.Value;
        });
        services.AddOpenTelemetry().WithLogging(b =>
            b.AddProcessor(new CaptureProcessor(r => captured = r.Attributes?.ToList() ?? new List<KeyValuePair<string, object>>())));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ILoggerFactory>().CreateLogger("test").LogError(exception, "boom");
        return captured;
    }

    private sealed class CaptureProcessor : BaseProcessor<LogRecord>
    {
        private readonly System.Action<LogRecord> _capture;
        public CaptureProcessor(System.Action<LogRecord> capture) => _capture = capture;
        public override void OnEnd(LogRecord data) => _capture(data);
    }

    private sealed class SeedProcessor : BaseProcessor<LogRecord>
    {
        private readonly IList<KeyValuePair<string, object>> _attrs;
        public SeedProcessor(IList<KeyValuePair<string, object>> attrs) => _attrs = attrs;
        public override void OnEnd(LogRecord data) => data.Attributes = (IReadOnlyList<KeyValuePair<string, object>>)_attrs;
    }
}
