using Bunit;
using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class Quilt4RadzenDataGridTests : BunitContext
{
    private readonly FakeContentService _contentService = new();
    private readonly FakeLanguageStateService _languageStateService = new();

    public Quilt4RadzenDataGridTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IContentService>(_contentService);
        Services.AddSingleton<ILanguageStateService>(_languageStateService);
        _languageStateService.Selected = new Language { Key = Guid.NewGuid(), Name = "English" };
    }

    [Fact]
    public void Loads_EmptyText_From_Content_Service()
    {
        _contentService.Map["grid.empty"] = "No customers yet.";

        var cut = Render<Quilt4RadzenDataGrid<string>>(p => p
            .Add(c => c.EmptyTextKey, "grid.empty")
            .Add(c => c.DefaultEmptyText, "No data."));

        cut.Instance.LoadedEmptyText.Should().Be("No customers yet.");
    }

    [Fact]
    public void Falls_Back_To_DefaultEmptyText()
    {
        var cut = Render<Quilt4RadzenDataGrid<string>>(p => p
            .Add(c => c.EmptyTextKey, "grid.missing")
            .Add(c => c.DefaultEmptyText, "No data."));

        cut.Instance.LoadedEmptyText.Should().Be("No data.");
    }

    [Fact]
    public void Updates_EmptyText_On_Language_Change()
    {
        _contentService.Map["grid.empty"] = "No customers yet.";

        var cut = Render<Quilt4RadzenDataGrid<string>>(p => p
            .Add(c => c.EmptyTextKey, "grid.empty")
            .Add(c => c.DefaultEmptyText, "No data."));

        cut.Instance.LoadedEmptyText.Should().Be("No customers yet.");

        _contentService.Map["grid.empty"] = "Inga kunder ännu.";
        _languageStateService.RaiseLanguageChanged();

        cut.WaitForState(() => cut.Instance.LoadedEmptyText == "Inga kunder ännu.");
        cut.Instance.LoadedEmptyText.Should().Be("Inga kunder ännu.");
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
