using Bunit;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Radzen;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

// Issue #141: the commonly-used components accept an optional name-keyed Translations dictionary and
// forward it to the content service (which sends it on the wire; the server applies it only on first
// creation). These tests assert the forwarding for both integration paths (direct GetContentAsync
// callers and PlaceholderResolver callers) and the split-button item model.
public class ComponentTranslationsTests : BunitContext
{
    private readonly CapturingContentService _content = new();

    public ComponentTranslationsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IContentService>(_content);
        Services.AddSingleton<IEditContentService>(new StubEditContentService());
        Services.AddSingleton<IContentSourceService>(new ContentSourceService());
        Services.AddSingleton<ILanguageStateService>(new StubLanguageState());
        Services.AddScoped<DialogService>();
    }

    [Fact]
    public void Quilt4Text_forwards_Translations_to_the_content_service()
    {
        var translations = new Dictionary<string, string> { ["Swedish"] = "Ärende" };

        Render<Quilt4Text>(p => p
            .Add(x => x.Key, "case.subject")
            .Add(x => x.Default, "Case subject")
            .Add(x => x.Translations, translations));

        _content.TranslationsSeen.Should().Contain(translations);
    }

    [Fact]
    public void Quilt4Text_without_Translations_forwards_null()
    {
        Render<Quilt4Text>(p => p
            .Add(x => x.Key, "case.subject")
            .Add(x => x.Default, "Case subject"));

        _content.TranslationsSeen.Should().OnlyContain(t => t == null);
    }

    [Fact]
    public void Quilt4RadzenPanelMenuItem_forwards_Translations()
    {
        var translations = new Dictionary<string, string> { ["Swedish"] = "Hem" };

        Render<Quilt4RadzenPanelMenuItem>(p => p
            .Add(x => x.TextKey, "menu.home")
            .Add(x => x.DefaultText, "Home")
            .Add(x => x.Translations, translations));

        _content.TranslationsSeen.Should().Contain(translations);
    }

    [Fact]
    public void Quilt4Tooltip_forwards_Translations_via_the_resolver()
    {
        var translations = new Dictionary<string, string> { ["Swedish"] = "Ta bort raden" };

        Render<Quilt4Tooltip>(p => p
            .Add(x => x.TooltipKey, "tip.delete")
            .Add(x => x.DefaultTooltip, "Delete this row")
            .Add(x => x.Translations, translations));

        _content.TranslationsSeen.Should().Contain(translations);
    }

    [Fact]
    public void Quilt4SplitButton_forwards_per_item_Translations()
    {
        var itemTranslations = new Dictionary<string, string> { ["Swedish"] = "Ta bort" };

        Render<Quilt4SplitButton>(p => p
            .Add(x => x.TextKey, "row.open")
            .Add(x => x.DefaultText, "Open")
            .Add(x => x.Items, new List<Quilt4SplitButtonItem>
            {
                new() { TextKey = "row.delete", DefaultText = "Delete", Value = "delete", Translations = itemTranslations },
            }));

        _content.TranslationsSeen.Should().Contain(itemTranslations);
    }

    // Issue #149: three components were missing Translations entirely — Quilt4Button and
    // Quilt4RadzenDataGridColumn covered 128 of the 129 unreachable usages FortDocs measured.
    // Passing Translations to a component that does not declare it compiles cleanly and throws at
    // render, so these tests exist as much to pin the parameter's existence as its forwarding.

    [Fact]
    public void Quilt4RadzenDataGridColumn_forwards_Translations()
    {
        var translations = new Dictionary<string, string> { ["Svenska"] = "Ärendenummer" };

        Render<Quilt4RadzenDataGridColumn<string>>(p => p
            .Add(x => x.TitleKey, "col.case.number")
            .Add(x => x.DefaultTitle, "Case number")
            .Add(x => x.Translations, translations));

        _content.TranslationsFor("col.case.number").Should().BeSameAs(translations);
    }

    [Fact]
    public void Quilt4RadzenDataGridColumn_without_Translations_forwards_null()
    {
        Render<Quilt4RadzenDataGridColumn<string>>(p => p
            .Add(x => x.TitleKey, "col.case.number")
            .Add(x => x.DefaultTitle, "Case number"));

        _content.TranslationsFor("col.case.number").Should().BeNull();
    }

    // The button resolves three separate keys, so it takes three separate dictionaries. A single
    // shared one could not distinguish "Spara" (label) from "Sparar…" (busy) from "Spara
    // dokumentet" (tooltip). The issue called out that a translated label with an untranslated
    // tooltip is worse than nothing, so all three are asserted together.
    [Fact]
    public void Quilt4Button_forwards_Translations_for_all_three_text_surfaces()
    {
        var text = new Dictionary<string, string> { ["Svenska"] = "Spara" };
        var busy = new Dictionary<string, string> { ["Svenska"] = "Sparar…" };
        var tooltip = new Dictionary<string, string> { ["Svenska"] = "Spara dokumentet" };

        Render<Quilt4Button>(p => p
            .Add(x => x.TextKey, "btn.save")
            .Add(x => x.DefaultText, "Save")
            .Add(x => x.Translations, text)
            .Add(x => x.BusyTextKey, "btn.save.busy")
            .Add(x => x.DefaultBusyText, "Saving…")
            .Add(x => x.BusyTextTranslations, busy)
            .Add(x => x.TooltipKey, "btn.save.tooltip")
            .Add(x => x.DefaultTooltip, "Save the document")
            .Add(x => x.TooltipTranslations, tooltip));

        _content.TranslationsFor("btn.save").Should().BeSameAs(text);
        _content.TranslationsFor("btn.save.busy").Should().BeSameAs(busy);
        _content.TranslationsFor("btn.save.tooltip").Should().BeSameAs(tooltip);
    }

    [Fact]
    public void Quilt4Button_keeps_the_three_dictionaries_separate()
    {
        var text = new Dictionary<string, string> { ["Svenska"] = "Spara" };

        Render<Quilt4Button>(p => p
            .Add(x => x.TextKey, "btn.save")
            .Add(x => x.DefaultText, "Save")
            .Add(x => x.Translations, text)
            .Add(x => x.BusyTextKey, "btn.save.busy")
            .Add(x => x.DefaultBusyText, "Saving…")
            .Add(x => x.TooltipKey, "btn.save.tooltip")
            .Add(x => x.DefaultTooltip, "Save the document"));

        // Only the label was given translations — the other two surfaces must not inherit it.
        _content.TranslationsFor("btn.save").Should().BeSameAs(text);
        _content.TranslationsFor("btn.save.busy").Should().BeNull();
        _content.TranslationsFor("btn.save.tooltip").Should().BeNull();
    }

    private sealed class CapturingContentService : IContentService
    {
        public List<IReadOnlyDictionary<string, string>> TranslationsSeen { get; } = new();

        /// <summary>Per-call (key, translations) pairs. Needed by the multi-surface components — a
        /// button resolves three different keys, so "which dictionary went with which key" is the
        /// assertion that matters, and an order-independent lookup keeps the test honest.</summary>
        public List<(string Key, IReadOnlyDictionary<string, string> Translations)> Calls { get; } = new();

        public IReadOnlyDictionary<string, string> TranslationsFor(string key)
            => Calls.Single(c => c.Key == key).Translations;

        public Task<(string Value, bool Success)> GetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
        {
            TranslationsSeen.Add(translations);
            Calls.Add((key, translations));
            return Task.FromResult((defaultValue ?? "", false));
        }
        public Task SetContentAsync(string key, string value, Guid languageKey, ContentFormat contentType, string application = null) => Task.CompletedTask;
        public Task ClearCacheAsync() => Task.CompletedTask;
    }

    private sealed class StubEditContentService : IEditContentService
    {
        public event EventHandler<EditModeEventArgs> EditModeEvent;
        public bool Enabled { get; set; }
        private void Unused() => EditModeEvent?.Invoke(this, null);
    }

    private sealed class StubLanguageState : ILanguageStateService
    {
        public event EventHandler<LanguageLoadedEventArgs> LanguageLoadedEvent;
        public event EventHandler<LanguageChangedEventArgs> LanguageChangedEvent;
        public event EventHandler<DeveloperModeEventArgs> DeveloperModeEvent;
        public Language Selected { get; set; } = new() { Name = "Swedish", Key = Guid.NewGuid() };
        public Language[] Languages { get; set; } = [];
        public bool DeveloperMode { get; set; }
        public Task<Language[]> ReloadAsync() => Task.FromResult(Languages);
        private void Unused() { LanguageLoadedEvent?.Invoke(this, null); LanguageChangedEvent?.Invoke(this, null); DeveloperModeEvent?.Invoke(this, null); }
    }
}
