using Bunit;
using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

// The four Quilt4Radzen* input wrappers (TextBox / TextArea / DropDown / Numeric) all share the
// same placeholder-resolution code via PlaceholderResolver. We test each component's wiring
// once to assert that wiring is correct, but exhaustive precedence tests live with the resolver
// implementation by virtue of the existing Quilt4RadzenDataGridColumnTests / AlertTests style.
public class Quilt4RadzenPlaceholderTests : BunitContext
{
    private readonly FakeContentService _contentService = new();
    private readonly FakeLanguageStateService _languageStateService = new();

    public Quilt4RadzenPlaceholderTests()
    {
        // RadzenDropDown / Numeric call JS interop in OnAfterRenderAsync; loose mode keeps the
        // unset calls from blowing up the test without requiring per-call mock setup.
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton<IContentService>(_contentService);
        Services.AddSingleton<ILanguageStateService>(_languageStateService);
        _languageStateService.Selected = new Language { Key = Guid.NewGuid(), Name = "English" };
    }

    [Fact]
    public void TextBox_loads_placeholder_from_content_service()
    {
        _contentService.Map["input.name"] = "Enter your name";

        var cut = Render<Quilt4RadzenTextBox>(p => p
            .Add(c => c.PlaceholderKey, "input.name")
            .Add(c => c.DefaultPlaceholder, "Name"));

        cut.Instance.LoadedPlaceholder.Should().Be("Enter your name");
    }

    [Fact]
    public void TextBox_falls_back_to_default_placeholder()
    {
        var cut = Render<Quilt4RadzenTextBox>(p => p
            .Add(c => c.PlaceholderKey, "input.missing")
            .Add(c => c.DefaultPlaceholder, "Type something..."));

        cut.Instance.LoadedPlaceholder.Should().Be("Type something...");
    }

    [Fact]
    public void TextArea_loads_placeholder_from_content_service()
    {
        _contentService.Map["input.notes"] = "Notes (optional)";

        var cut = Render<Quilt4RadzenTextArea>(p => p
            .Add(c => c.PlaceholderKey, "input.notes")
            .Add(c => c.DefaultPlaceholder, "Notes"));

        cut.Instance.LoadedPlaceholder.Should().Be("Notes (optional)");
    }

    [Fact]
    public void TextArea_falls_back_to_default_placeholder()
    {
        var cut = Render<Quilt4RadzenTextArea>(p => p
            .Add(c => c.PlaceholderKey, "input.missing")
            .Add(c => c.DefaultPlaceholder, "Type your message..."));

        cut.Instance.LoadedPlaceholder.Should().Be("Type your message...");
    }

    [Fact]
    public void DropDown_loads_placeholder_from_content_service()
    {
        _contentService.Map["input.country"] = "Choose country";

        var cut = Render<Quilt4RadzenDropDown<string>>(p => p
            .Add(c => c.PlaceholderKey, "input.country")
            .Add(c => c.DefaultPlaceholder, "Country"));

        cut.Instance.LoadedPlaceholder.Should().Be("Choose country");
    }

    [Fact]
    public void Numeric_loads_placeholder_from_content_service()
    {
        _contentService.Map["input.quantity"] = "How many?";

        var cut = Render<Quilt4RadzenNumeric<int>>(p => p
            .Add(c => c.PlaceholderKey, "input.quantity")
            .Add(c => c.DefaultPlaceholder, "Quantity"));

        cut.Instance.LoadedPlaceholder.Should().Be("How many?");
    }

    [Fact]
    public void TextBox_updates_placeholder_on_language_change()
    {
        // One language-change test is enough to validate the shared subscription pattern
        // — all four wrappers use the same OnInitializedAsync handler shape.
        _contentService.Map["input.name"] = "Enter your name";

        var cut = Render<Quilt4RadzenTextBox>(p => p
            .Add(c => c.PlaceholderKey, "input.name")
            .Add(c => c.DefaultPlaceholder, "Name"));

        cut.Instance.LoadedPlaceholder.Should().Be("Enter your name");

        _contentService.Map["input.name"] = "Ange ditt namn";
        _languageStateService.RaiseLanguageChanged();

        cut.WaitForState(() => cut.Instance.LoadedPlaceholder == "Ange ditt namn");
        cut.Instance.LoadedPlaceholder.Should().Be("Ange ditt namn");
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
