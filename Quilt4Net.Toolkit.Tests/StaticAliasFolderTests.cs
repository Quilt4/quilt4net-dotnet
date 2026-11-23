using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class StaticAliasFolderTests
{
    [Fact]
    public void Fold_with_no_aliases_returns_input_unchanged()
    {
        var raw = MakeView(("Quilt4Net.Server", "Production", "1.0.0"));

        var folded = StaticAliasFolder.Fold(raw, []);

        folded.Should().BeSameAs(raw);
    }

    [Fact]
    public void Fold_with_null_aliases_returns_input_unchanged()
    {
        var raw = MakeView(("Quilt4Net.Server", "Production", "1.0.0"));

        var folded = StaticAliasFolder.Fold(raw, null);

        folded.Should().BeSameAs(raw);
    }

    [Fact]
    public void Fold_collapses_multiple_source_names_into_one_logical_row()
    {
        var raw = MakeView(
            ("Quilt4Net.Server", "Production", "1.0.0"),
            ("Quilt4Net.Server.Client", "Production", "1.0.0"));

        var folded = StaticAliasFolder.Fold(raw,
        [
            new ApplicationAliasMap
            {
                LogicalName = "quilt4net-web",
                SourceNames = ["Quilt4Net.Server", "Quilt4Net.Server.Client"]
            }
        ]);

        folded.Applications.Should().Equal("quilt4net-web");
        folded.TryGetCell("quilt4net-web", "Production", out var cell).Should().BeTrue();
        cell.Version.Should().Be("1.0.0");
        folded.TryGetAlias("quilt4net-web", "Production", out var alias).Should().BeTrue();
        alias.SourceNames.Should().BeEquivalentTo("Quilt4Net.Server", "Quilt4Net.Server.Client");
        alias.HasConflict.Should().BeFalse();
    }

    [Fact]
    public void Fold_surfaces_conflict_when_sources_disagree_on_version()
    {
        var raw = MakeView(
            ("Quilt4Net.Server", "Production", "1.0.0", DateTime.UtcNow.AddHours(-2)),
            ("Quilt4Net.Server.Client", "Production", "1.1.0", DateTime.UtcNow.AddHours(-1)));

        var folded = StaticAliasFolder.Fold(raw,
        [
            new ApplicationAliasMap
            {
                LogicalName = "quilt4net-web",
                SourceNames = ["Quilt4Net.Server", "Quilt4Net.Server.Client"]
            }
        ]);

        folded.TryGetCell("quilt4net-web", "Production", out var cell).Should().BeTrue();
        cell.Version.Should().Be("1.1.0"); // most recent wins
        folded.TryGetAlias("quilt4net-web", "Production", out var alias).Should().BeTrue();
        alias.HasConflict.Should().BeTrue();
        alias.ConflictingVersions.Should().HaveCount(2);
    }

    [Fact]
    public void Fold_passes_unmapped_names_through_unchanged_and_does_not_emit_alias_info()
    {
        var raw = MakeView(("Some.Other.App", "Production", "2.0.0"));

        var folded = StaticAliasFolder.Fold(raw,
        [
            new ApplicationAliasMap { LogicalName = "quilt4net-web", SourceNames = ["Quilt4Net.Server"] }
        ]);

        folded.Applications.Should().Equal("Some.Other.App");
        folded.TryGetAlias("Some.Other.App", "Production", out _).Should().BeFalse();
    }

    private static VersionMatrixView MakeView(params (string app, string env, string version)[] cells)
        => MakeView(cells.Select(c => (c.app, c.env, c.version, DateTime.UtcNow)).ToArray());

    private static VersionMatrixView MakeView(params (string app, string env, string version, DateTime lastSeen)[] cells)
    {
        var matrixCells = cells
            .Select(c => new VersionMatrixCell
            {
                ApplicationName = c.app,
                Environment = c.env,
                Version = c.version,
                LastSeen = c.lastSeen,
                Source = VersionMatrixSource.Log
            })
            .ToArray();
        return VersionMatrixView.FromCells(matrixCells);
    }
}
