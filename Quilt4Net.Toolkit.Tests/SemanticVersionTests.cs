using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.ReleaseNotes;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Ordering is the whole release-note feature — "everything newer than the version I last showed"
/// is an ordering question, and the sort key is how the server answers it with an index instead of
/// a scan. So these tests are about precedence, not parsing trivia.
/// </summary>
public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.9.0", "1.10.0")]     // the one text ordering gets wrong, and the reason for this type
    [InlineData("1.0.9", "1.0.10")]
    [InlineData("9.0.0", "10.0.0")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.0", "1.1.0")]
    [InlineData("1.0.0", "2.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0")]              // a pre-release precedes its release
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]      // more identifiers wins when the prefix matches
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")] // numeric identifiers rank below alphanumeric
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]     // numeric identifiers compare numerically
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1")]
    [InlineData("1.0.0-9", "1.0.0--x")]               // an identifier that merely starts with '-' is alphanumeric
    public void Lower_precedence_sorts_before_higher(string lower, string higher)
    {
        var a = SemanticVersion.Parse(lower);
        var b = SemanticVersion.Parse(higher);

        a.Should().BeLessThan(b);
        // The sort key is what the database orders by, so it has to agree with CompareTo — if these
        // two ever diverge, the API returns one answer and the query another.
        string.CompareOrdinal(a.SortKey, b.SortKey).Should().BeNegative();
    }

    [Fact]
    public void A_list_of_versions_sorts_the_way_a_human_would_read_it()
    {
        var versions = new[] { "1.10.0", "1.2.0", "2.0.0-rc.1", "1.9.9", "2.0.0", "1.2.0-beta.2" }
            .Select(SemanticVersion.Parse)
            .Order()
            .Select(x => x.Original);

        versions.Should().Equal("1.2.0-beta.2", "1.2.0", "1.9.9", "1.10.0", "2.0.0-rc.1", "2.0.0");
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0+build.7")]      // build metadata is ignored for precedence
    [InlineData("1.0.0+a", "1.0.0+b")]
    public void Build_metadata_does_not_change_precedence(string left, string right)
    {
        SemanticVersion.Parse(left).CompareTo(SemanticVersion.Parse(right)).Should().Be(0);
    }

    [Theory]
    [InlineData("1.4.0", false)]
    [InlineData("2.0.0-beta.1", true)]
    [InlineData("1.0.0-0", true)]
    public void A_pre_release_says_so(string value, bool expected)
    {
        SemanticVersion.Parse(value).IsPreRelease.Should().Be(expected);
    }

    [Theory]
    [InlineData("v1.2.3")]        // the tag spelling: common, and still not SemVer
    [InlineData("1.2")]           // two-part
    [InlineData("1.2.3.4")]       // the four-part assembly version
    [InlineData("01.2.3")]        // leading zeros
    [InlineData("1.2.3-01")]      // leading zeros in a numeric pre-release identifier
    [InlineData("1.2.3-")]        // empty pre-release
    [InlineData("1.2.3-alpha..1")]// empty identifier
    [InlineData("1.2.3-alpha_1")] // underscore is not in the alphabet
    [InlineData("latest")]
    [InlineData("")]
    [InlineData(null)]
    public void Near_misses_are_refused_rather_than_guessed_at(string value)
    {
        SemanticVersion.TryParse(value, out var parsed).Should().BeFalse();
        parsed.Should().BeNull();
    }

    [Fact]
    public void The_refusal_says_what_was_expected()
    {
        // The message is the whole user experience of a failed publish from a pipeline, so it names
        // both the value and the shape wanted.
        var act = () => SemanticVersion.Parse("v1.2.3");

        act.Should().Throw<FormatException>().WithMessage("*v1.2.3*MAJOR.MINOR.PATCH*");
    }

    [Fact]
    public void The_version_is_echoed_back_exactly_as_written()
    {
        var parsed = SemanticVersion.Parse(" 1.2.3-beta.1+build.9 ");

        parsed.Original.Should().Be("1.2.3-beta.1+build.9");
        parsed.PreRelease.Should().Be("beta.1");
        parsed.Build.Should().Be("build.9");
        parsed.ToString().Should().Be("1.2.3-beta.1+build.9");
    }

    [Fact]
    public void A_component_too_large_for_the_sort_key_is_refused()
    {
        // Ten digits is what keeps every component the same width, and a same-width key is the only
        // reason an ordinal range query means what it says. A number that does not fit is refused at
        // the door rather than silently mis-sorted.
        SemanticVersion.TryParse("9999999999.0.0", out _).Should().BeTrue();
        SemanticVersion.TryParse("10000000000.0.0", out _).Should().BeFalse();
    }
}
