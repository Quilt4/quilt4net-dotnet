using AwesomeAssertions;
using Quilt4Net.Toolkit.Blazor.Features.Log;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class LogFiltersTests
{
    [Fact]
    public void Empty_filter_matches_everything()
    {
        var f = LogFilters.Empty;

        f.IsEmpty.Should().BeTrue();
        f.MatchesSource(LogSource.Trace).Should().BeTrue();
        f.MatchesLevel(SeverityLevel.Error).Should().BeTrue();
        f.MatchesApplication("anything").Should().BeTrue();
        f.MatchesEnvironment("anything").Should().BeTrue();
    }

    [Fact]
    public void Populated_dimension_keeps_matching_values_drops_others()
    {
        var f = new LogFilters
        {
            Sources = [LogSource.Exception],
            Levels = [SeverityLevel.Warning, SeverityLevel.Error]
        };

        f.IsEmpty.Should().BeFalse();
        f.MatchesSource(LogSource.Exception).Should().BeTrue();
        f.MatchesSource(LogSource.Trace).Should().BeFalse();
        f.MatchesLevel(SeverityLevel.Error).Should().BeTrue();
        f.MatchesLevel(SeverityLevel.Warning).Should().BeTrue();
        f.MatchesLevel(SeverityLevel.Information).Should().BeFalse();
    }

    [Fact]
    public void Application_filter_works_against_logical_names_supplied_by_caller()
    {
        // The component is responsible for resolving raw → logical before calling Matches.
        var f = new LogFilters { Applications = ["quilt4net-web"] };

        f.MatchesApplication("quilt4net-web").Should().BeTrue();
        f.MatchesApplication("Quilt4Net.Server").Should().BeFalse(); // raw name not in filter
    }

    [Fact]
    public void Environment_filter_treats_null_or_empty_as_empty_string()
    {
        var f = new LogFilters { Environments = [""] };

        f.MatchesEnvironment(null).Should().BeTrue();
        f.MatchesEnvironment("").Should().BeTrue();
        f.MatchesEnvironment("Production").Should().BeFalse();
    }

    [Fact]
    public void Filters_compose_independently_across_dimensions()
    {
        var f = new LogFilters
        {
            Sources = [LogSource.Exception],
            Levels = [SeverityLevel.Error],
            Applications = ["app-a"],
            Environments = ["Production"]
        };

        // Row that matches every dimension
        (f.MatchesSource(LogSource.Exception)
            && f.MatchesLevel(SeverityLevel.Error)
            && f.MatchesApplication("app-a")
            && f.MatchesEnvironment("Production"))
            .Should().BeTrue();

        // One mismatched dimension → row is filtered out
        (f.MatchesSource(LogSource.Trace)
            && f.MatchesLevel(SeverityLevel.Error)
            && f.MatchesApplication("app-a")
            && f.MatchesEnvironment("Production"))
            .Should().BeFalse();
    }
}
