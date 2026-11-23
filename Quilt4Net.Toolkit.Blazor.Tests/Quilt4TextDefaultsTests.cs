using System.Globalization;
using Bunit;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quilt4Net.Toolkit.Blazor;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Radzen;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

// Issue #135: authoritative per-language default text. A content key can declare a default per
// language (at least en + sv); the default shown before/without a stored translation is chosen by:
// active-UI-culture code default -> English ("en" or the single Default) -> key. Backward compatible
// with the single-Default flow.
public class Quilt4TextDefaultsTests
{
    [Theory]
    [InlineData("sv", "Ärendemening")]   // active culture matches
    [InlineData("nb", "Case subject")]   // active culture missing -> English "en"
    public void Resolver_picks_active_culture_then_english(string culture, string expected)
    {
        var defaults = new Dictionary<string, string> { ["en"] = "Case subject", ["sv"] = "Ärendemening" };

        LanguageDefaultResolver.Resolve(defaults, invariantDefault: null, key: "case.subject", activeCultureCode: culture)
            .Should().Be(expected);
    }

    [Fact]
    public void Resolver_is_case_insensitive_on_language_code()
    {
        var defaults = new Dictionary<string, string> { ["SV"] = "Ärendemening" };

        LanguageDefaultResolver.Resolve(defaults, null, "case.subject", "sv").Should().Be("Ärendemening");
    }

    [Fact]
    public void Resolver_falls_back_to_single_default_as_english_then_key()
    {
        // No dictionary match and no "en" entry -> the single invariant Default acts as English.
        LanguageDefaultResolver.Resolve(new Dictionary<string, string> { ["de"] = "Betreff" }, "Case subject", "case.subject", "sv")
            .Should().Be("Case subject");

        // Nothing at all -> the key.
        LanguageDefaultResolver.Resolve(null, null, "case.subject", "sv").Should().Be("case.subject");
    }

    [Fact]
    public void Quilt4Text_renders_active_culture_default_when_no_stored_value()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("sv");
            using var ctx = new BunitContext();
            ctx.Services.AddSingleton<IContentService>(new NotFoundContentService());
            ctx.Services.AddSingleton<IEditContentService>(new StubEditContentService());
            ctx.Services.AddSingleton<IContentSourceService>(new ContentSourceService());
            ctx.Services.AddSingleton<ILanguageStateService>(new StubLanguageState());
            ctx.Services.AddScoped<DialogService>();

            var cut = ctx.Render<Quilt4Text>(p => p
                .Add(x => x.Key, "case.subject")
                .Add(x => x.Default, "Case subject")
                .Add(x => x.Defaults, new Dictionary<string, string> { ["en"] = "Case subject", ["sv"] = "Ärendemening" }));

            cut.Markup.Should().Contain("Ärendemening",
                "on a Swedish UI culture the authoritative Swedish default must render before any stored translation exists");
            cut.Markup.Should().NotContain("Case subject");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void Quilt4Text_prefers_stored_value_over_per_language_default()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("sv");
            using var ctx = new BunitContext();
            ctx.Services.AddSingleton<IContentService>(new StoredValueContentService("Diarienummer (stored)"));
            ctx.Services.AddSingleton<IEditContentService>(new StubEditContentService());
            ctx.Services.AddSingleton<IContentSourceService>(new ContentSourceService());
            ctx.Services.AddSingleton<ILanguageStateService>(new StubLanguageState());
            ctx.Services.AddScoped<DialogService>();

            var cut = ctx.Render<Quilt4Text>(p => p
                .Add(x => x.Key, "case.diary")
                .Add(x => x.Defaults, new Dictionary<string, string> { ["sv"] = "Diarienummer (default)" }));

            cut.Markup.Should().Contain("Diarienummer (stored)");
            cut.Markup.Should().NotContain("(default)");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    private sealed class NotFoundContentService : IContentService
    {
        public Task<(string Value, bool Success)> GetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
            => Task.FromResult((defaultValue ?? "", false));
        public Task SetContentAsync(string key, string value, Guid languageKey, ContentFormat contentType, string application = null) => Task.CompletedTask;
        public Task ClearCacheAsync() => Task.CompletedTask;
    }

    private sealed class StoredValueContentService : IContentService
    {
        private readonly string _stored;
        public StoredValueContentService(string stored) => _stored = stored;
        public Task<(string Value, bool Success)> GetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
            => Task.FromResult((_stored, true));
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

        private void Unused()
        {
            LanguageLoadedEvent?.Invoke(this, null);
            LanguageChangedEvent?.Invoke(this, null);
            DeveloperModeEvent?.Invoke(this, null);
        }
    }
}
