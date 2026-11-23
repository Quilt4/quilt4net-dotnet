using Bunit;
using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class Quilt4RadzenPanelMenuItemTests : BunitContext
{
    private readonly FakeContentService _contentService = new();
    private readonly FakeLanguageStateService _languageStateService = new();

    public Quilt4RadzenPanelMenuItemTests()
    {
        Services.AddSingleton<IContentService>(_contentService);
        Services.AddSingleton<ILanguageStateService>(_languageStateService);
        _languageStateService.Selected = new Language { Key = Guid.NewGuid(), Name = "English" };
    }

    [Fact]
    public void Loads_Text_From_Content_Service()
    {
        _contentService.SetResult("Dashboard");

        var cut = Render<Quilt4RadzenPanelMenuItem>(parameters => parameters
            .Add(p => p.TextKey, "menu.dashboard")
            .Add(p => p.DefaultText, "Home")
            .Add(p => p.Icon, "dashboard")
            .Add(p => p.Path, "/dashboard"));

        cut.Instance.LoadedText.Should().Be("Dashboard");
    }

    [Fact]
    public void Falls_Back_To_Default_When_Content_Not_Found()
    {
        _contentService.SetResult(null, success: false);

        var cut = Render<Quilt4RadzenPanelMenuItem>(parameters => parameters
            .Add(p => p.TextKey, "menu.missing")
            .Add(p => p.DefaultText, "Fallback Menu"));

        cut.Instance.LoadedText.Should().Be("Fallback Menu");
    }

    [Fact]
    public void Updates_Text_On_Language_Change()
    {
        _contentService.SetResult("Settings");

        var cut = Render<Quilt4RadzenPanelMenuItem>(parameters => parameters
            .Add(p => p.TextKey, "menu.settings")
            .Add(p => p.DefaultText, "Settings"));

        cut.Instance.LoadedText.Should().Be("Settings");

        _contentService.SetResult("Inställningar");
        _languageStateService.RaiseLanguageChanged();

        cut.WaitForState(() => cut.Instance.LoadedText == "Inställningar");
        cut.Instance.LoadedText.Should().Be("Inställningar");
    }

    [Fact]
    public void Uses_Default_When_Content_Returns_Empty()
    {
        _contentService.SetResult("", success: true);

        var cut = Render<Quilt4RadzenPanelMenuItem>(parameters => parameters
            .Add(p => p.TextKey, "menu.empty")
            .Add(p => p.DefaultText, "Default Item"));

        cut.Instance.LoadedText.Should().Be("Default Item");
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
            return Task.FromResult((defaultValue ?? "", _success));
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
