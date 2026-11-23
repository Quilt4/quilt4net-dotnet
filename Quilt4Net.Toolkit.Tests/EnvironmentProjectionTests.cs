using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class EnvironmentProjectionTests
{
    [Fact]
    public void Reads_OTel_resource_attribute_first()
    {
        // AddQuilt4NetLogging emits "deployment.environment" via OpenTelemetry resource
        // attributes; that path should win when both keys are present.
        var deploymentIdx = ApplicationInsightsService.EnvironmentProjection.IndexOf("\"deployment.environment\"", System.StringComparison.Ordinal);
        var aspNetCoreIdx = ApplicationInsightsService.EnvironmentProjection.IndexOf("\"AspNetCoreEnvironment\"", System.StringComparison.Ordinal);

        deploymentIdx.Should().BeGreaterThan(0, "deployment.environment must appear in the projection");
        aspNetCoreIdx.Should().BeGreaterThan(0, "AspNetCoreEnvironment must appear as a fallback");
        deploymentIdx.Should().BeLessThan(aspNetCoreIdx, "OTel resource attribute must be coalesced before the legacy key");
    }

    [Fact]
    public void Uses_coalesce_so_either_key_resolves_a_value()
    {
        ApplicationInsightsService.EnvironmentProjection
            .Should().StartWith("coalesce(", "the projection must coalesce both keys, not pick only one");
    }

    [Fact]
    public void Casts_each_key_to_string_for_safe_KQL_consumption()
    {
        ApplicationInsightsService.EnvironmentProjection
            .Should().Contain("tostring(_p[\"deployment.environment\"])")
            .And.Contain("tostring(_p[\"AspNetCoreEnvironment\"])");
    }
}
