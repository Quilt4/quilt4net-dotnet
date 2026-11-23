using AwesomeAssertions;
using Quilt4Net.Toolkit.Blazor;
using Quilt4Net.Toolkit.Features.Content;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

/// <summary>
/// #144: a host stores its own language identity as an ISO code and wants the content language to
/// follow it. Before <see cref="Language.Code"/> it could only match on the display name (which a
/// rename or localisation breaks) or hardcode the per-tenant <see cref="Language.Key"/> (which does
/// not survive a move between teams or environments).
/// </summary>
public class LanguageSelectByCodeTests
{
    private static readonly Language English = new() { Key = Guid.Empty, Name = "English", Code = "en" };
    private static readonly Language Swedish = new() { Key = Guid.NewGuid(), Name = "Svenska", Code = "sv" };
    private static readonly Language Uncoded = new() { Key = Guid.NewGuid(), Name = "Klingon" };

    [Fact]
    public void SelectByCode_selects_the_matching_language()
    {
        var sut = State(English, Swedish);

        sut.SelectByCode("sv").Should().BeTrue();

        sut.Selected.Should().Be(Swedish);
    }

    [Theory]
    [InlineData("SV")]
    [InlineData("Sv")]
    [InlineData(" sv ")]
    public void SelectByCode_matches_case_insensitively_and_ignores_surrounding_space(string code)
    {
        // A host passing a culture string it got from elsewhere should not have to normalise first.
        var sut = State(English, Swedish);

        sut.SelectByCode(code).Should().BeTrue();

        sut.Selected.Should().Be(Swedish);
    }

    [Fact]
    public void A_miss_leaves_the_current_selection_untouched()
    {
        // The outcome that matters most: a team simply may not have the language the host asked
        // for. Swapping the user's language on a miss would be worse than doing nothing.
        var sut = State(English, Swedish);
        sut.Selected = Swedish;

        sut.SelectByCode("de").Should().BeFalse();

        sut.Selected.Should().Be(Swedish);
    }

    [Fact]
    public void A_language_with_no_code_is_never_matched()
    {
        // Null means "the server could not determine one", not "matches anything" — treating it
        // loosely would reintroduce the guessing the code exists to remove.
        var sut = State(English, Uncoded);

        sut.SelectByCode("tlh").Should().BeFalse();

        sut.Selected.Should().Be(English);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_request_is_a_miss_not_a_crash(string code)
    {
        var sut = State(English, Swedish);

        sut.SelectByCode(code).Should().BeFalse();
        sut.SelectByName(code).Should().BeFalse();
    }

    [Fact]
    public void SelectByName_still_works_for_a_host_that_only_has_a_name()
    {
        var sut = State(English, Swedish);

        sut.SelectByName("svenska").Should().BeTrue();

        sut.Selected.Should().Be(Swedish);
    }

    private static ILanguageStateService State(params Language[] languages)
        => new StubLanguageState { Languages = languages, Selected = languages[0] };

    /// <summary>
    /// Deliberately implements only the members the default interface methods rely on — which is
    /// also the proof that a host's own implementation keeps compiling without writing them.
    /// </summary>
    private sealed class StubLanguageState : ILanguageStateService
    {
        public Language Selected { get; set; }
        public Language[] Languages { get; set; }
        public bool DeveloperMode { get; set; }
        public Task<Language[]> ReloadAsync() => Task.FromResult(Languages);

#pragma warning disable CS0067
        public event EventHandler<LanguageLoadedEventArgs> LanguageLoadedEvent;
        public event EventHandler<LanguageChangedEventArgs> LanguageChangedEvent;
        public event EventHandler<DeveloperModeEventArgs> DeveloperModeEvent;
#pragma warning restore CS0067
    }
}
