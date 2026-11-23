using Bunit;
using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class Quilt4TooltipTests : BunitContext
{
    private readonly FakeContentService _contentService = new();
    private readonly FakeLanguageStateService _languageStateService = new();

    public Quilt4TooltipTests()
    {
        Services.AddSingleton<IContentService>(_contentService);
        Services.AddSingleton<ILanguageStateService>(_languageStateService);
        _languageStateService.Selected = new Language { Key = Guid.NewGuid(), Name = "English" };
    }

    [Fact]
    public void Loads_tooltip_from_content_service()
    {
        _contentService.Map["btn.delete.tooltip"] = "Delete this customer";

        var cut = Render<Quilt4Tooltip>(p => p
            .Add(c => c.TooltipKey, "btn.delete.tooltip")
            .Add(c => c.DefaultTooltip, "Delete"));

        cut.Instance.LoadedTooltip.Should().Be("Delete this customer");
        // Verify the title attribute is on the wrapping span — that's how browsers surface
        // the native hover-tooltip.
        cut.Markup.Should().Contain("title=\"Delete this customer\"");
    }

    [Fact]
    public void Falls_back_to_default_tooltip()
    {
        var cut = Render<Quilt4Tooltip>(p => p
            .Add(c => c.TooltipKey, "missing.key")
            .Add(c => c.DefaultTooltip, "Fallback hover text"));

        cut.Instance.LoadedTooltip.Should().Be("Fallback hover text");
    }

    [Fact]
    public void Wraps_child_content()
    {
        _contentService.Map["btn.help.tooltip"] = "Open help";

        var cut = Render<Quilt4Tooltip>(p => p
            .Add(c => c.TooltipKey, "btn.help.tooltip")
            .Add(c => c.DefaultTooltip, "Help")
            .AddChildContent("<button>?</button>"));

        cut.Markup.Should().Contain("<button>?</button>", "tooltip wrapper must render its child content unchanged");
        cut.Markup.Should().Contain("title=\"Open help\"");
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
