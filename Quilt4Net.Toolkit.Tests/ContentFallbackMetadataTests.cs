using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Content;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// The fallback metadata contract, and specifically the three-valued <c>CanImprove</c>.
/// <para>
/// The question a caller actually has is "will asking again later do better?". Getting that wrong in
/// the safe-looking direction is the trap: a plain <c>bool</c> would report an older server — which
/// says nothing at all — as a confident "no", which is indistinguishable from a translation that has
/// genuinely given up.
/// </para>
/// </summary>
public class ContentFallbackMetadataTests
{
    private static ContentResult Result(ContentFallbackReason reason) => new()
    {
        Value = "v", Success = true, Source = ContentSource.Server, Stale = false, FallbackReason = reason,
    };

    [Fact]
    public void A_queued_translation_is_the_only_reason_that_may_improve()
    {
        Result(ContentFallbackReason.TranslationPending).CanImprove.Should().BeTrue();
    }

    [Theory]
    [InlineData(ContentFallbackReason.TranslationFailed)]
    [InlineData(ContentFallbackReason.TranslationDisabled)]
    [InlineData(ContentFallbackReason.NoContent)]
    [InlineData(ContentFallbackReason.None)]
    public void Every_other_known_reason_is_a_dead_end(ContentFallbackReason reason)
    {
        Result(reason).CanImprove.Should().BeFalse();
    }

    [Fact]
    public void An_unreported_reason_is_null_not_false()
    {
        // "I don't know" is a different claim from "no better result is coming". An older server
        // omits the field, and reporting that as a confident false would make a pending translation
        // look like one that had given up.
        Result(ContentFallbackReason.Unknown).CanImprove.Should().BeNull();
    }

    [Fact]
    public void Unknown_is_the_zero_value_so_an_absent_field_deserializes_to_it()
    {
        // The whole back-compat story rests on this: a server that predates the field sends no
        // property, System.Text.Json leaves the enum at default, and default must mean "not known".
        ((int)ContentFallbackReason.Unknown).Should().Be(0);

        var fromOlderServer = new ContentResult { Value = "v", Success = true, Source = ContentSource.Server, Stale = false };
        fromOlderServer.FallbackReason.Should().Be(ContentFallbackReason.Unknown);
        fromOlderServer.ServedLanguageKey.Should().BeNull();
        fromOlderServer.IsStageFallback.Should().BeFalse();
        fromOlderServer.CanImprove.Should().BeNull();
    }

    [Fact]
    public void Stage_fallback_is_independent_of_the_language_reason()
    {
        // A value can be both from a lower stage and in the wrong language, which is why these are
        // two fields rather than one enum.
        var both = Result(ContentFallbackReason.TranslationPending) with { IsStageFallback = true };

        both.IsStageFallback.Should().BeTrue();
        both.FallbackReason.Should().Be(ContentFallbackReason.TranslationPending);
        both.CanImprove.Should().BeTrue();
    }
}
