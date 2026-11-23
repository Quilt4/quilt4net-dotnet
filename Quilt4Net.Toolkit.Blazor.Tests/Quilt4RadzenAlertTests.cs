using Bunit;
using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class Quilt4RadzenAlertTests : BunitContext
{
    private readonly FakeContentService _contentService = new();
    private readonly FakeLanguageStateService _languageStateService = new();

    public Quilt4RadzenAlertTests()
    {
        Services.AddSingleton<IContentService>(_contentService);
        Services.AddSingleton<ILanguageStateService>(_languageStateService);
        _languageStateService.Selected = new Language { Key = Guid.NewGuid(), Name = "English" };
    }

    [Fact]
    public void Loads_Text_From_Content_Service()
    {
        _contentService.Map["alert.permission"] = "You don't have permission to do that.";

        var cut = Render<Quilt4RadzenAlert>(parameters => parameters
            .Add(p => p.TextKey, "alert.permission")
            .Add(p => p.DefaultText, "Permission denied."));

        cut.Instance.LoadedText.Should().Be("You don't have permission to do that.");
    }

    [Fact]
    public void Falls_Back_To_DefaultText_When_Content_Missing()
    {
        // Same fallback semantics as the other Quilt4Radzen* wrappers — if the service has no
        // value for the key, the static default carries the user-facing message.
        var cut = Render<Quilt4RadzenAlert>(parameters => parameters
            .Add(p => p.TextKey, "alert.missing")
            .Add(p => p.DefaultText, "Something went wrong."));

        cut.Instance.LoadedText.Should().Be("Something went wrong.");
    }

    [Fact]
    public void Loads_Title_From_Content_Service_When_TitleKey_Set()
    {
        // Title is optional — a separate key/default pair, resolved with the same precedence.
        // A title-less alert leaves both TitleKey and DefaultTitle null and gets null on Loaded.
        _contentService.Map["alert.title.error"] = "Error";

        var cut = Render<Quilt4RadzenAlert>(parameters => parameters
            .Add(p => p.TextKey, "alert.body")
            .Add(p => p.DefaultText, "body")
            .Add(p => p.TitleKey, "alert.title.error")
            .Add(p => p.DefaultTitle, "Default Error"));

        cut.Instance.LoadedTitle.Should().Be("Error");
    }

    [Fact]
    public void Title_Is_Null_When_No_TitleKey_Or_DefaultTitle_Supplied()
    {
        var cut = Render<Quilt4RadzenAlert>(parameters => parameters
            .Add(p => p.TextKey, "alert.body")
            .Add(p => p.DefaultText, "body"));

        cut.Instance.LoadedTitle.Should().BeNull("a title-less alert must not render an empty title bar");
    }

    [Fact]
    public void Updates_On_Language_Change()
    {
        _contentService.Map["alert.permission"] = "Permission denied";

        var cut = Render<Quilt4RadzenAlert>(parameters => parameters
            .Add(p => p.TextKey, "alert.permission")
            .Add(p => p.DefaultText, "Permission denied."));

        cut.Instance.LoadedText.Should().Be("Permission denied");

        _contentService.Map["alert.permission"] = "Åtkomst nekad";
        _languageStateService.RaiseLanguageChanged();

        cut.WaitForState(() => cut.Instance.LoadedText == "Åtkomst nekad");
        cut.Instance.LoadedText.Should().Be("Åtkomst nekad");
    }

    [Fact]
    public void DefaultText_Used_When_TextKey_Null()
    {
        // Pure-default usage — a host that wants a Quilt4 alert without a content lookup
        // can still mount this component and get its DefaultText rendered.
        var cut = Render<Quilt4RadzenAlert>(parameters => parameters
            .Add(p => p.DefaultText, "Static message"));

        cut.Instance.LoadedText.Should().Be("Static message");
    }

    private class FakeContentService : IContentService
    {
        public Dictionary<string, string> Map { get; } = new();

        public Task<(string Value, bool Success)> GetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
        {
            if (Map.TryGetValue(key ?? "", out var value) && !string.IsNullOrEmpty(value))
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

        public void RaiseLanguageChanged() => LanguageChangedEvent?.Invoke(this, new LanguageChangedEventArgs());
        private void Unused() { LanguageLoadedEvent?.Invoke(this, null); DeveloperModeEvent?.Invoke(this, null); }
    }
}
