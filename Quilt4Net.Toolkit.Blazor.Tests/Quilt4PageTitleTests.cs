using Bunit;
using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class Quilt4PageTitleTests : BunitContext
{
    private readonly FakeContentService _contentService = new();
    private readonly FakeLanguageStateService _languageStateService = new();

    public Quilt4PageTitleTests()
    {
        Services.AddSingleton<IContentService>(_contentService);
        Services.AddSingleton<ILanguageStateService>(_languageStateService);
    }

    [Fact]
    public void Renders_Title_From_Content_Service()
    {
        _contentService.SetResult("Welcome Page");

        var cut = Render<Quilt4PageTitle>(parameters => parameters
            .Add(p => p.Key, "page.home.title")
            .Add(p => p.Default, "Fallback"));

        cut.Instance.Title.Should().Be("Welcome Page");
    }

    [Fact]
    public void Renders_Default_When_Content_Not_Found()
    {
        _contentService.SetResult(null, success: false);

        var cut = Render<Quilt4PageTitle>(parameters => parameters
            .Add(p => p.Key, "page.missing.title")
            .Add(p => p.Default, "My Fallback Title"));

        cut.Instance.Title.Should().Be("My Fallback Title");
    }

    [Fact]
    public void Updates_Title_On_Language_Change()
    {
        _contentService.SetResult("English Title");

        var cut = Render<Quilt4PageTitle>(parameters => parameters
            .Add(p => p.Key, "page.home.title")
            .Add(p => p.Default, "Fallback"));

        cut.Instance.Title.Should().Be("English Title");

        _contentService.SetResult("Svensk Titel");
        _languageStateService.RaiseLanguageChanged();

        cut.WaitForState(() => cut.Instance.Title == "Svensk Titel");
        cut.Instance.Title.Should().Be("Svensk Titel");
    }

    [Fact]
    public void Renders_Default_When_Content_Returns_Empty()
    {
        _contentService.SetResult("", success: true);

        var cut = Render<Quilt4PageTitle>(parameters => parameters
            .Add(p => p.Key, "page.empty.title")
            .Add(p => p.Default, "Default Title"));

        cut.Instance.Title.Should().Be("Default Title");
    }

    private class FakeContentService : IContentService
    {
        private string _value = "";
        private bool _success = true;

        public void SetResult(string value, bool success = true)
        {
            _value = value;
            _success = success;
        }

        public Task<(string Value, bool Success)> GetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
        {
            if (_success && !string.IsNullOrEmpty(_value))
                return Task.FromResult((_value, true));
            return Task.FromResult((defaultValue, _success));
        }

        public Task SetContentAsync(string key, string value, Guid languageKey, ContentFormat contentType, string application = null) => Task.CompletedTask;
        public Task ClearCacheAsync() => Task.CompletedTask;
    }

    private class FakeLanguageStateService : ILanguageStateService
    {
        public event EventHandler<LanguageLoadedEventArgs> LanguageLoadedEvent;
        public event EventHandler<LanguageChangedEventArgs> LanguageChangedEvent;
        public event EventHandler<DeveloperModeEventArgs> DeveloperModeEvent;
        public Language Selected { get; set; }
        public Language[] Languages { get; set; } = [];
        public bool DeveloperMode { get; set; }
        public Task<Language[]> ReloadAsync() => Task.FromResult(Languages);

        public void RaiseLanguageChanged()
        {
            LanguageChangedEvent?.Invoke(this, new LanguageChangedEventArgs());
        }
    }
}
