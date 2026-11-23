using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Logging;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Issue #86 — when a consumer deploys the same compiled service under multiple logical
/// names, telemetry needs <c>service.instance.id</c> to disambiguate them. The resolver
/// reads from a precedence chain so consumers can pick the source that fits their hosting
/// environment (option lambda for code-based config, OTel-standard env var for OTel-aware
/// platforms, Quilt4Net shorthand for hosts that don't want to construct the multi-key
/// OTel string by hand).
///
/// These tests pin the precedence and the back-compat null fallback. The resolver is
/// pure (env reader injected) so no real env-var manipulation is needed.
/// </summary>
public class ServiceInstanceIdResolverTests
{
    [Fact]
    public void Option_value_wins_over_both_env_vars()
    {
        var env = Env(("OTEL_RESOURCE_ATTRIBUTES", "service.instance.id=fromOtel"),
                      ("QUILT4NET_SERVICE_INSTANCE_ID", "fromShorthand"));

        ServiceInstanceIdResolver.Resolve("fromOption", env)
            .Should().Be("fromOption");
    }

    [Fact]
    public void OTEL_RESOURCE_ATTRIBUTES_wins_when_option_is_null()
    {
        var env = Env(("OTEL_RESOURCE_ATTRIBUTES", "service.instance.id=fromOtel"),
                      ("QUILT4NET_SERVICE_INSTANCE_ID", "fromShorthand"));

        ServiceInstanceIdResolver.Resolve(null, env)
            .Should().Be("fromOtel");
    }

    [Fact]
    public void OTEL_RESOURCE_ATTRIBUTES_extracts_only_the_service_instance_id_pair()
    {
        // The env var is comma-separated key=value pairs per the OTel spec — make sure
        // we pluck out the right one and ignore the others.
        var env = Env(("OTEL_RESOURCE_ATTRIBUTES",
            "service.namespace=production, service.instance.id=Thargelion, deployment.environment=CI"));

        ServiceInstanceIdResolver.Resolve(null, env)
            .Should().Be("Thargelion");
    }

    [Fact]
    public void OTEL_RESOURCE_ATTRIBUTES_pair_lookup_is_case_insensitive_for_the_key()
    {
        var env = Env(("OTEL_RESOURCE_ATTRIBUTES", "Service.Instance.Id=fromOtel"));

        ServiceInstanceIdResolver.Resolve(null, env)
            .Should().Be("fromOtel");
    }

    [Fact]
    public void QUILT4NET_shorthand_env_wins_when_option_and_OTEL_are_absent()
    {
        var env = Env(("QUILT4NET_SERVICE_INSTANCE_ID", "fromShorthand"));

        ServiceInstanceIdResolver.Resolve(null, env)
            .Should().Be("fromShorthand");
    }

    [Fact]
    public void Returns_null_when_nothing_is_configured_so_callers_keep_back_compat_fallback()
    {
        ServiceInstanceIdResolver.Resolve(null, Env())
            .Should().BeNull();
    }

    [Fact]
    public void Whitespace_only_option_value_is_treated_as_unset()
    {
        var env = Env(("QUILT4NET_SERVICE_INSTANCE_ID", "fromShorthand"));

        ServiceInstanceIdResolver.Resolve("   ", env)
            .Should().Be("fromShorthand");
    }

    [Fact]
    public void Empty_OTEL_pair_value_is_skipped_and_falls_through_to_shorthand()
    {
        var env = Env(("OTEL_RESOURCE_ATTRIBUTES", "service.instance.id="),
                      ("QUILT4NET_SERVICE_INSTANCE_ID", "fromShorthand"));

        ServiceInstanceIdResolver.Resolve(null, env)
            .Should().Be("fromShorthand");
    }

    [Fact]
    public void Trims_surrounding_whitespace_from_the_resolved_value()
    {
        ServiceInstanceIdResolver.Resolve("  Thargelion  ", Env())
            .Should().Be("Thargelion");
    }

    private static Func<string, string> Env(params (string Key, string Value)[] pairs)
    {
        var dict = pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        return name => dict.TryGetValue(name, out var v) ? v : null;
    }
}
