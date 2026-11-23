using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Diagnostics;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class IncidentIdTests
{
    [Fact]
    public void New_returns_id_of_default_length_6()
    {
        var id = IncidentId.New();
        id.Length.Should().Be(6);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(12)]
    public void New_honors_requested_length(int length)
    {
        var id = IncidentId.New(length);
        id.Length.Should().Be(length);
    }

    [Fact]
    public void New_uses_unambiguous_alphabet_only()
    {
        for (var i = 0; i < 200; i++)
        {
            var id = IncidentId.New(8);
            id.Should().MatchRegex("^[2-9A-HJ-NP-Z]+$",
                "ids must avoid ambiguous characters 0/1/I/O");
        }
    }

    [Fact]
    public void New_returns_distinct_values_across_calls()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => IncidentId.New()).ToHashSet();
        ids.Count.Should().BeGreaterThan(95, "with 6-char ids over 32 alphabet, near-100 distinct out of 100 is expected");
    }

    [Fact]
    public void New_throws_for_non_positive_length()
    {
        var act = () => IncidentId.New(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
