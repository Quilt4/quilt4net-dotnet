using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class VersionMatrixViewTests
{
    [Fact]
    public void FromCells_returns_empty_collections_for_empty_input()
    {
        var view = VersionMatrixView.FromCells([]);

        view.Applications.Should().BeEmpty();
        view.Environments.Should().BeEmpty();
        view.Cells.Should().BeEmpty();
        view.LastRefreshedUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void FromCells_orders_applications_alphabetically_case_insensitive()
    {
        var view = VersionMatrixView.FromCells(
        [
            Cell("Zoo.App", "Production"),
            Cell("alpha.app", "Production"),
            Cell("Beta.App", "Production"),
        ]);

        view.Applications.Should().Equal("alpha.app", "Beta.App", "Zoo.App");
    }

    [Fact]
    public void FromCells_returns_environments_alphabetical_for_render_time_ordering()
    {
        var view = VersionMatrixView.FromCells(
        [
            Cell("app", "Production"),
            Cell("app", "Test"),
            Cell("app", "Staging"),
            Cell("app", "Development"),
            Cell("app", "Sandbox"),
        ]);

        view.Environments.Should().Equal("Development", "Production", "Sandbox", "Staging", "Test");
    }

    [Fact]
    public void FromCells_treats_empty_environment_as_unknown_in_cell_key()
    {
        var view = VersionMatrixView.FromCells([Cell("app", "", "1.0.0")]);

        view.TryGetCell("app", "(unknown)", out var c).Should().BeTrue();
        c.Version.Should().Be("1.0.0");
    }

    [Fact]
    public void FromCells_picks_most_recent_when_duplicates_for_same_cell()
    {
        var view = VersionMatrixView.FromCells(
        [
            Cell("app", "Production", "1.0.0", DateTime.UtcNow.AddHours(-5)),
            Cell("app", "Production", "1.2.0", DateTime.UtcNow.AddHours(-1)),
            Cell("app", "Production", "1.1.0", DateTime.UtcNow.AddHours(-3)),
        ]);

        view.TryGetCell("app", "Production", out var picked).Should().BeTrue();
        picked.Version.Should().Be("1.2.0");
    }

    private static VersionMatrixCell Cell(string app, string env, string version = "1.0.0", DateTime? lastSeen = null) =>
        new()
        {
            ApplicationName = app,
            Environment = env,
            Version = version,
            LastSeen = lastSeen ?? DateTime.UtcNow,
            Source = VersionMatrixSource.Log
        };
}
