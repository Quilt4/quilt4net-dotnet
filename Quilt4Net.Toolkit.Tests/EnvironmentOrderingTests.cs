using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class EnvironmentOrderingTests
{
    [Fact]
    public void Order_uses_default_order_when_no_preferred_supplied()
    {
        var ordered = EnvironmentOrdering.Order(["Production", "Test", "Staging", "Development"]);

        ordered.Should().Equal("Development", "Staging", "Test", "Production");
    }

    [Fact]
    public void Order_places_unknown_named_envs_after_known_alphabetical()
    {
        var ordered = EnvironmentOrdering.Order(["Sandbox", "Production", "Acceptance"]);

        ordered.Should().Equal("Production", "Acceptance", "Sandbox");
    }

    [Fact]
    public void Order_places_unknown_environment_marker_last()
    {
        var ordered = EnvironmentOrdering.Order(["Production", "", "Development"]);

        ordered.Should().Equal("Development", "Production", "(unknown)");
    }

    [Fact]
    public void Order_honors_supplied_preferred_order()
    {
        var ordered = EnvironmentOrdering.Order(
            ["Production", "Test", "CI", "Development"],
            ["Development", "CI", "Test", "Production"]);

        ordered.Should().Equal("Development", "CI", "Test", "Production");
    }

    [Fact]
    public void Order_with_supplied_preferred_still_ranks_unlisted_after_listed_alphabetical()
    {
        var ordered = EnvironmentOrdering.Order(
            ["Production", "Sandbox", "QA", "Development"],
            ["Development", "Production"]);

        ordered.Should().Equal("Development", "Production", "QA", "Sandbox");
    }

    [Fact]
    public void Order_is_case_insensitive_for_distinct_and_ranking()
    {
        var ordered = EnvironmentOrdering.Order(
            ["PRODUCTION", "production", "Development"],
            ["development", "PRODUCTION"]);

        ordered.Should().Equal("Development", "PRODUCTION");
    }
}
