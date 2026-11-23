using AwesomeAssertions;
using Quilt4Net.Toolkit.Blazor.Features.Configuration;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

/// <summary>
/// The admin grid lists every configuration entry the team's API key can read, so the same key
/// appears once per application × environment × instance. These are the rules that make those rows
/// distinguishable — and, critically, that stop the environment filter from hiding the shared
/// entries that apply everywhere.
/// </summary>
public class ConfigurationScopeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_shared_scope_value_renders_as_an_explicit_marker_not_a_blank(string value)
    {
        // Application/Environment/Instance are declared `required string` but a shared entry
        // legitimately arrives null. A blank cell reads as missing data rather than "applies to all".
        ConfigurationScope.Label(value).Should().Be("(all)");
    }

    [Fact]
    public void A_scoped_value_renders_as_itself()
    {
        ConfigurationScope.Label("Production").Should().Be("Production");
    }

    [Fact]
    public void Available_environments_are_distinct_sorted_and_exclude_the_shared_ones()
    {
        var entries = new[]
        {
            Entry(environment: "Production"),
            Entry(environment: "Development"),
            Entry(environment: "production"),
            Entry(environment: null),
            Entry(environment: ""),
        };

        ConfigurationScope.AvailableEnvironments(entries)
            .Should().Equal("Development", "Production");
    }

    [Fact]
    public void The_default_selection_is_the_host_environment()
    {
        ConfigurationScope.DefaultEnvironment(["Development", "Production"], "Production")
            .Should().Be("Production");
    }

    [Fact]
    public void The_default_selection_matches_the_host_environment_case_insensitively()
    {
        ConfigurationScope.DefaultEnvironment(["Development", "Production"], "PRODUCTION")
            .Should().Be("Production");
    }

    [Fact]
    public void No_entries_for_the_host_environment_means_no_filter_rather_than_an_arbitrary_one()
    {
        // LogEnvironmentSelector falls back to the first option, but that view is read-only. Picking
        // an arbitrary environment here would hide configuration the operator can edit — the exact
        // failure this grid exists to fix.
        ConfigurationScope.DefaultEnvironment(["Development", "Production"], "Staging")
            .Should().BeNull();
    }

    [Fact]
    public void No_host_environment_at_all_means_no_filter()
    {
        // A Blazor WebAssembly host has no IHostEnvironment to resolve.
        ConfigurationScope.DefaultEnvironment(["Development"], null).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_selection_matches_everything(string selected)
    {
        ConfigurationScope.MatchesEnvironment("Production", selected).Should().BeTrue();
        ConfigurationScope.MatchesEnvironment(null, selected).Should().BeTrue();
    }

    [Fact]
    public void A_selection_keeps_its_own_environment_and_drops_the_others()
    {
        ConfigurationScope.MatchesEnvironment("Production", "Production").Should().BeTrue();
        ConfigurationScope.MatchesEnvironment("Development", "Production").Should().BeFalse();
    }

    [Fact]
    public void Environment_matching_is_case_insensitive()
    {
        ConfigurationScope.MatchesEnvironment("production", "Production").Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_shared_entry_survives_every_environment_filter(string entryEnvironment)
    {
        // The whole complaint is that scope was invisible. A filter that also hid the entries
        // applying to *every* environment would conceal more than the missing columns ever did.
        ConfigurationScope.MatchesEnvironment(entryEnvironment, "Production").Should().BeTrue();
    }

    [Fact]
    public void The_instance_column_stays_hidden_when_nothing_carries_an_instance()
    {
        // Instance is a level slated for removal, so it earns no permanent column.
        var entries = new[] { Entry(instance: null), Entry(instance: "") };

        ConfigurationScope.ShowInstance(entries).Should().BeFalse();
    }

    [Fact]
    public void The_instance_column_appears_as_soon_as_one_entry_carries_an_instance()
    {
        // Otherwise two entries differing only by instance are indistinguishable again.
        var entries = new[] { Entry(instance: null), Entry(instance: "worker-1") };

        ConfigurationScope.ShowInstance(entries).Should().BeTrue();
    }

    private static ConfigurationResponse Entry(string environment = null, string instance = null)
    {
        return new ConfigurationResponse
        {
            Key = "SomeKey",
            Application = null,
            Environment = environment,
            Instance = instance,
            Value = "true",
            DefaultValue = "true",
            ValueType = "Boolean",
            LastUsed = null,
            Ttl = null,
        };
    }
}
