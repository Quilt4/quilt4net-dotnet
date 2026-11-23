using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Content;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

/// <summary>
/// How the debug overlay renders a language fallback. Shown only while
/// <c>IContentSourceService.Enabled</c> — the components already gate on that.
/// </summary>
public class ContentFallbackDecorationTests
{
    private static ContentResult Result(ContentFallbackReason reason, ContentSource source = ContentSource.Server) => new()
    {
        Value = "v", Success = true, Source = source, Stale = false, FallbackReason = reason,
    };

    [Fact]
    public void A_language_fallback_is_dashed_so_the_source_colour_keeps_its_meaning()
    {
        // The value can be a perfectly fresh server hit and still be the wrong language — two
        // independent dimensions, so the second one must not repaint the first.
        var style = ContentSourceDecoration.Style(Result(ContentFallbackReason.TranslationPending));

        style.Should().Contain("dashed");
        style.Should().Contain("#2e7d32", "it is still a fresh server value");
    }

    [Fact]
    public void No_fallback_stays_solid()
    {
        ContentSourceDecoration.Style(Result(ContentFallbackReason.None)).Should().Contain("solid");
    }

    [Fact]
    public void An_older_server_does_not_make_everything_look_like_a_fallback()
    {
        // Unknown means the server said nothing. Decorating on that would light up every value in
        // the app against a server that predates the field.
        ContentSourceDecoration.Style(Result(ContentFallbackReason.Unknown)).Should().Contain("solid");
        ContentSourceDecoration.Tooltip(Result(ContentFallbackReason.Unknown)).Should().NotContain("Language:");
    }

    [Fact]
    public void A_pending_translation_says_a_later_load_may_show_it()
    {
        ContentSourceDecoration.Tooltip(Result(ContentFallbackReason.TranslationPending))
            .Should().Contain("later load may show it");
    }

    [Fact]
    public void A_dead_end_says_waiting_will_not_help()
    {
        ContentSourceDecoration.Tooltip(Result(ContentFallbackReason.TranslationFailed))
            .Should().Contain("Waiting will not help");
    }

    [Theory]
    [InlineData(ContentFallbackReason.TranslationDisabled)]
    [InlineData(ContentFallbackReason.NoContent)]
    public void Every_language_fallback_explains_itself(ContentFallbackReason reason)
    {
        ContentSourceDecoration.Tooltip(Result(reason)).Should().Contain("Language:");
    }

    [Fact]
    public void Stage_fallback_is_reported_alongside_the_language_reason()
    {
        var tooltip = ContentSourceDecoration.Tooltip(
            Result(ContentFallbackReason.TranslationPending) with { IsStageFallback = true });

        tooltip.Should().Contain("Stage:");
        tooltip.Should().Contain("Language:");
    }

    [Fact]
    public void The_source_line_is_always_kept()
    {
        // The fallback lines are additive — the existing "where did this come from" answer must not
        // be displaced by the new "what is it" answer.
        ContentSourceDecoration.Tooltip(Result(ContentFallbackReason.NoContent, ContentSource.Cache))
            .Should().StartWith("Source: local cache");
    }
}
