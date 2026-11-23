using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class Quilt4ContentServiceTests
{
    [Fact]
    public async Task GetAsync_with_translations_forwards_default_selected_language_and_translations()
    {
        var content = new CapturingContentService("Ärende");
        var language = new StubLanguageState();
        var sut = new Quilt4ContentService(content, language);

        var translations = new Dictionary<string, string> { ["Swedish"] = "Ärende" };
        var value = await sut.GetAsync("case.subject", "Case subject", translations);

        value.Should().Be("Ärende");
        content.LastDefaultValue.Should().Be("Case subject");
        content.LastLanguageKey.Should().Be(language.Selected.Key);
        content.LastTranslations.Should().BeSameAs(translations);
    }

    [Fact]
    public async Task GetAsync_two_arg_overload_sends_no_translations()
    {
        var content = new CapturingContentService("Case subject");
        var sut = new Quilt4ContentService(content, new StubLanguageState());

        await sut.GetAsync("case.subject", "Case subject");

        content.LastTranslations.Should().BeNull();
    }

    private sealed class CapturingContentService : IContentService
    {
        private readonly string _value;
        public string LastDefaultValue { get; private set; }
        public Guid LastLanguageKey { get; private set; }
        public IReadOnlyDictionary<string, string> LastTranslations { get; private set; }
        public CapturingContentService(string value) => _value = value;

        public Task<(string Value, bool Success)> GetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
        {
            LastDefaultValue = defaultValue;
            LastLanguageKey = languageKey;
            LastTranslations = translations;
            return Task.FromResult((_value, true));
        }
        public Task SetContentAsync(string key, string value, Guid languageKey, ContentFormat contentType, string application = null) => Task.CompletedTask;
        public Task ClearCacheAsync() => Task.CompletedTask;
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
