using Bunit;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quilt4Net.Toolkit.Blazor;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Radzen;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

// Content source indicator: when IContentSourceService.Enabled is set, content components annotate
// each rendered value with its provenance (outline + tooltip). Off, markup must be unchanged.
public class ContentSourceIndicatorTests : BunitContext
{
    [Fact]
    public void Overlay_is_absent_when_mode_is_off()
    {
        var cut = RenderText(ContentSource.Server, sourceModeEnabled: false);

        cut.Markup.Should().NotContain("outline: 2px solid",
            "with the overlay off the component must render exactly as before");
        cut.Markup.Should().NotContain("Source:");
    }

    [Fact]
    public void A_fallback_default_is_flagged_as_default()
    {
        var cut = RenderText(ContentSource.Default, sourceModeEnabled: true);

        cut.Markup.Should().Contain("#c62828", "a fallback default is the case being hunted, so it gets the red outline");
        cut.Markup.Should().Contain("the server has no value for this key");
    }

    [Fact]
    public void A_server_value_is_flagged_as_server()
    {
        var cut = RenderText(ContentSource.Server, sourceModeEnabled: true);

        cut.Markup.Should().Contain("#2e7d32");
        cut.Markup.Should().Contain("Source: server");
    }

    [Fact]
    public void A_cache_hit_is_flagged_as_cache()
    {
        var cut = RenderText(ContentSource.Cache, sourceModeEnabled: true);

        cut.Markup.Should().Contain("#1565c0");
        cut.Markup.Should().Contain("Source: local cache (value from the server)");
    }

    [Fact]
    public void Toggling_the_mode_restyles_an_already_rendered_component()
    {
        var sourceService = new ContentSourceService();
        var cut = RenderText(ContentSource.Default, sourceModeEnabled: false, sourceService);

        cut.Markup.Should().NotContain("outline: 2px solid");

        sourceService.Enabled = true;

        cut.Markup.Should().Contain("outline: 2px solid",
            "components subscribe to SourceModeEvent, so toggling must restyle without a reload");
    }

    [Fact]
    public void Quilt4Span_annotates_its_source()
    {
        Services.AddSingleton<IContentService>(new SourceContentService(ContentSource.StaleCache));
        Services.AddSingleton<IEditContentService>(new StubEditContentService());
        Services.AddSingleton<IContentSourceService>(new ContentSourceService { Enabled = true });
        Services.AddSingleton<ILanguageStateService>(new StubLanguageState());

        var cut = Render<Quilt4Span>(p => p.Add(x => x.Key, "a.key").Add(x => x.Default, "a-default"));

        cut.Markup.Should().Contain("#ef6c00");
        cut.Markup.Should().Contain("past its TTL");
    }

    // Quilt4Raw normally renders its value with no wrapping element. The overlay needs an element to
    // hang the outline on, so it adds one — but only while the mode is on.
    [Fact]
    public void Quilt4Raw_only_wraps_its_value_while_the_mode_is_on()
    {
        Services.AddSingleton<IContentService>(new SourceContentService(ContentSource.Server));
        Services.AddSingleton<IEditContentService>(new StubEditContentService());
        var sourceService = new ContentSourceService();
        Services.AddSingleton<IContentSourceService>(sourceService);
        Services.AddSingleton<ILanguageStateService>(new StubLanguageState());

        var cut = Render<Quilt4Raw>(p => p.Add(x => x.Key, "a.key").Add(x => x.Default, "a-default"));

        cut.Markup.Should().NotContain("<span", "off, the value renders bare exactly as before");

        sourceService.Enabled = true;

        cut.Markup.Should().Contain("<span");
        cut.Markup.Should().Contain("#2e7d32");
    }

    [Fact]
    public void Edit_mode_takes_precedence_over_the_source_overlay()
    {
        Services.AddSingleton<IContentService>(new SourceContentService(ContentSource.Default));
        Services.AddSingleton<IEditContentService>(new StubEditContentService { Enabled = true });
        Services.AddSingleton<IContentSourceService>(new ContentSourceService { Enabled = true });
        Services.AddSingleton<ILanguageStateService>(new StubLanguageState());
        Services.AddScoped<DialogService>();

        var cut = Render<Quilt4Text>(p => p.Add(x => x.Key, "a.key").Add(x => x.Default, "a-default"));

        cut.Markup.Should().Contain("#ffc0cb",
            "edit mode is interactive and its cursor affordance must survive; the read-only overlay yields");
        cut.Markup.Should().NotContain("outline: 2px solid");
    }

    private IRenderedComponent<Quilt4Text> RenderText(ContentSource source, bool sourceModeEnabled, ContentSourceService sourceService = null)
    {
        Services.AddSingleton<IContentService>(new SourceContentService(source));
        Services.AddSingleton<IEditContentService>(new StubEditContentService());
        Services.AddSingleton<IContentSourceService>(sourceService ?? new ContentSourceService { Enabled = sourceModeEnabled });
        Services.AddSingleton<ILanguageStateService>(new StubLanguageState());
        Services.AddScoped<DialogService>();

        return Render<Quilt4Text>(p => p.Add(x => x.Key, "a.key").Add(x => x.Default, "a-default"));
    }

    // Overrides GetContentResultAsync so the component sees a chosen provenance. Note the interface's
    // default implementation would report Unknown — only implementations that genuinely know their
    // source override it.
    private sealed class SourceContentService : IContentService
    {
        private readonly ContentSource _source;
        public SourceContentService(ContentSource source) => _source = source;

        public Task<(string Value, bool Success)> GetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
            => Task.FromResult((defaultValue ?? "value", true));

        public Task<ContentResult> GetContentResultAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
            => Task.FromResult(new ContentResult { Value = defaultValue ?? "value", Success = true, Source = _source, Stale = false });

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

        public Language Selected { get; set; } = new() { Name = "English", Key = Guid.NewGuid() };
        public Language[] Languages { get; set; } = [];
        public bool DeveloperMode { get; set; }

        public Task<Language[]> ReloadAsync() => Task.FromResult(Languages);

        private void Unused()
        {
            LanguageLoadedEvent?.Invoke(this, null);
            LanguageChangedEvent?.Invoke(this, null);
            DeveloperModeEvent?.Invoke(this, null);
        }
    }
}
