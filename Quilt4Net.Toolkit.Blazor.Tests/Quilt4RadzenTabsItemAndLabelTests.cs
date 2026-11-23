using Bunit;
using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

// Bundle the TabsItem + Label tests since they share the identical content-resolution
// pattern (same Quilt4RadzenPanelMenuItem-style TextKey/DefaultText pair, same resolver).
public class Quilt4RadzenTabsItemAndLabelTests : BunitContext
{
    private readonly FakeContentService _contentService = new();
    private readonly FakeLanguageStateService _languageStateService = new();

    public Quilt4RadzenTabsItemAndLabelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IContentService>(_contentService);
        Services.AddSingleton<ILanguageStateService>(_languageStateService);
        _languageStateService.Selected = new Language { Key = Guid.NewGuid(), Name = "English" };
    }

    [Fact]
    public void TabsItem_loads_text_from_content_service()
    {
        _contentService.Map["tab.overview"] = "Overview";

        var cut = Render<Quilt4RadzenTabsItem>(p => p
            .Add(c => c.TextKey, "tab.overview")
            .Add(c => c.DefaultText, "Default Overview"));

        cut.Instance.LoadedText.Should().Be("Overview");
    }

    [Fact]
    public void TabsItem_falls_back_to_default()
    {
        var cut = Render<Quilt4RadzenTabsItem>(p => p
            .Add(c => c.TextKey, "tab.missing")
            .Add(c => c.DefaultText, "Fallback Tab"));

        cut.Instance.LoadedText.Should().Be("Fallback Tab");
    }

    [Fact]
    public void Label_loads_text_from_content_service()
    {
        _contentService.Map["form.name.label"] = "Full name";

        var cut = Render<Quilt4RadzenLabel>(p => p
            .Add(c => c.TextKey, "form.name.label")
            .Add(c => c.DefaultText, "Name"));

        cut.Instance.LoadedText.Should().Be("Full name");
    }

    [Fact]
    public void Label_falls_back_to_default()
    {
        var cut = Render<Quilt4RadzenLabel>(p => p
            .Add(c => c.TextKey, "form.missing")
            .Add(c => c.DefaultText, "Default Label"));

        cut.Instance.LoadedText.Should().Be("Default Label");
    }

    [Fact]
    public void Label_updates_on_language_change()
    {
        _contentService.Map["form.name.label"] = "Full name";

        var cut = Render<Quilt4RadzenLabel>(p => p
            .Add(c => c.TextKey, "form.name.label")
            .Add(c => c.DefaultText, "Name"));

        cut.Instance.LoadedText.Should().Be("Full name");

        _contentService.Map["form.name.label"] = "Fullständigt namn";
        _languageStateService.RaiseLanguageChanged();

        cut.WaitForState(() => cut.Instance.LoadedText == "Fullständigt namn");
        cut.Instance.LoadedText.Should().Be("Fullständigt namn");
    }

    private class FakeContentService : IContentService
    {
        public Dictionary<string, string> Map { get; } = new();

        public Task<(string Value, bool Success)> GetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
        {
            if (Map.TryGetValue(key ?? "", out var v) && !string.IsNullOrEmpty(v)) return Task.FromResult((v, true));
            return Task.FromResult((defaultValue ?? "", false));
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
        public void RaiseLanguageChanged() => LanguageChangedEvent?.Invoke(this, new LanguageChangedEventArgs());
        private void Unused() { LanguageLoadedEvent?.Invoke(this, null); DeveloperModeEvent?.Invoke(this, null); }
    }
}
