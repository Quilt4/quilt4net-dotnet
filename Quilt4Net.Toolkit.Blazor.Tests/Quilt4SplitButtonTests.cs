using Bunit;
using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class Quilt4SplitButtonTests : BunitContext
{
    private readonly FakeContentService _contentService = new();
    private readonly FakeLanguageStateService _languageStateService = new();

    public Quilt4SplitButtonTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IContentService>(_contentService);
        Services.AddSingleton<ILanguageStateService>(_languageStateService);
        _languageStateService.Selected = new Language { Key = Guid.NewGuid(), Name = "English" };
    }

    [Fact]
    public void Renders_primary_text_from_content_service()
    {
        _contentService.Set("split.save", "Save Changes");

        var cut = Render<Quilt4SplitButton>(parameters => parameters
            .Add(p => p.TextKey, "split.save")
            .Add(p => p.DefaultText, "Save"));

        cut.Markup.Should().Contain("Save Changes");
    }

    [Fact]
    public void Renders_primary_default_when_content_missing()
    {
        var cut = Render<Quilt4SplitButton>(parameters => parameters
            .Add(p => p.TextKey, "split.missing")
            .Add(p => p.DefaultText, "Fallback Primary"));

        cut.Markup.Should().Contain("Fallback Primary");
    }

    [Fact]
    public void Renders_menu_items_with_localized_text()
    {
        _contentService.Set("item.edit", "Edit");
        _contentService.Set("item.delete", "Delete");

        var cut = Render<Quilt4SplitButton>(parameters => parameters
            .Add(p => p.TextKey, "split.actions")
            .Add(p => p.DefaultText, "Actions")
            .Add(p => p.Items, new List<Quilt4SplitButtonItem>
            {
                new() { TextKey = "item.edit", DefaultText = "Edit default", Value = "edit" },
                new() { TextKey = "item.delete", DefaultText = "Delete default", Value = "delete" },
            }));

        cut.Markup.Should().Contain("Edit");
        cut.Markup.Should().Contain("Delete");
    }

    [Fact]
    public void Item_falls_back_to_default_when_content_missing()
    {
        var cut = Render<Quilt4SplitButton>(parameters => parameters
            .Add(p => p.TextKey, "split.actions")
            .Add(p => p.DefaultText, "Actions")
            .Add(p => p.Items, new List<Quilt4SplitButtonItem>
            {
                new() { TextKey = "item.missing", DefaultText = "Item Fallback", Value = "x" },
            }));

        cut.Markup.Should().Contain("Item Fallback");
    }

    [Fact]
    public void Updates_text_on_language_change()
    {
        _contentService.Set("split.save", "Save");

        var cut = Render<Quilt4SplitButton>(parameters => parameters
            .Add(p => p.TextKey, "split.save")
            .Add(p => p.DefaultText, "Save"));

        cut.Markup.Should().Contain("Save");

        _contentService.Set("split.save", "Spara");
        _languageStateService.RaiseLanguageChanged();

        cut.WaitForState(() => cut.Markup.Contains("Spara"));
        cut.Markup.Should().Contain("Spara");
    }

    [Fact]
    public void Disabled_disables_the_primary_button()
    {
        _contentService.Set("split.save", "Save");

        var cut = Render<Quilt4SplitButton>(parameters => parameters
            .Add(p => p.TextKey, "split.save")
            .Add(p => p.DefaultText, "Save")
            .Add(p => p.Disabled, true));

        cut.FindAll("button").Should().Contain(b => b.HasAttribute("disabled"));
    }

    [Fact]
    public void Busy_shows_the_busy_text()
    {
        var cut = Render<Quilt4SplitButton>(parameters => parameters
            .Add(p => p.TextKey, "split.save")
            .Add(p => p.DefaultText, "Save")
            .Add(p => p.Busy, true)
            .Add(p => p.BusyTextKey, "split.save.busy")
            .Add(p => p.DefaultBusyText, "Saving…"));

        cut.Markup.Should().Contain("Saving…");
    }

    [Fact]
    public void Primary_click_invokes_Click_handler()
    {
        _contentService.Set("split.save", "Save");
        var clicked = false;

        var cut = Render<Quilt4SplitButton>(parameters => parameters
            .Add(p => p.TextKey, "split.save")
            .Add(p => p.DefaultText, "Save")
            .Add(p => p.Click, () => { clicked = true; return Task.CompletedTask; }));

        cut.FindAll("button")[0].Click();

        clicked.Should().BeTrue();
    }

    private class FakeContentService : IContentService
    {
        private readonly Dictionary<string, string> _values = new();

        public void Set(string key, string value) => _values[key] = value;

        public Task<(string Value, bool Success)> GetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
        {
            if (!string.IsNullOrEmpty(key) && _values.TryGetValue(key, out var value))
                return Task.FromResult((value, true));
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

        public void RaiseLanguageChanged()
        {
            LanguageChangedEvent?.Invoke(this, new LanguageChangedEventArgs());
        }
    }
}
