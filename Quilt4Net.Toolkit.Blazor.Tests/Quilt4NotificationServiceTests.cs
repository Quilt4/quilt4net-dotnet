using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Radzen;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class Quilt4NotificationServiceTests
{
    [Fact]
    public async Task NotifyAsync_resolves_summary_and_detail_keys()
    {
        var content = new FakeContentService();
        content.Map["save.success.summary"] = "Saved";
        content.Map["save.success.detail"] = "Your changes have been saved.";
        var languageState = new FakeLanguageStateService { Selected = new Language { Key = Guid.NewGuid() } };
        var notificationService = new NotificationService();

        var sut = new Quilt4NotificationService(notificationService, content, languageState);

        await sut.NotifyAsync(NotificationSeverity.Success,
            summaryKey: "save.success.summary", defaultSummary: "Saved (default)",
            detailKey: "save.success.detail", defaultDetail: "Default detail.");

        // Radzen's NotificationService exposes a `Messages` observable collection rather than
        // an OnNotify event; the NotificationContainer subscribes to it. Asserting on that
        // collection captures the same data the UI would render.
        notificationService.Messages.Should().HaveCount(1);
        var msg = notificationService.Messages[0];
        msg.Severity.Should().Be(NotificationSeverity.Success);
        msg.Summary.Should().Be("Saved");
        msg.Detail.Should().Be("Your changes have been saved.");
    }

    [Fact]
    public async Task NotifyAsync_falls_back_to_default_summary_when_key_missing()
    {
        var content = new FakeContentService();
        var languageState = new FakeLanguageStateService { Selected = new Language { Key = Guid.NewGuid() } };
        var notificationService = new NotificationService();

        var sut = new Quilt4NotificationService(notificationService, content, languageState);

        await sut.NotifyAsync(NotificationSeverity.Warning,
            summaryKey: "missing.key", defaultSummary: "Fallback summary");

        notificationService.Messages.Should().HaveCount(1);
        var msg = notificationService.Messages[0];
        msg.Summary.Should().Be("Fallback summary");
        msg.Detail.Should().BeNull("when neither detail key nor default is supplied, leave Detail null");
    }

    [Fact]
    public async Task NotifyAsync_forwards_duration()
    {
        var content = new FakeContentService();
        var languageState = new FakeLanguageStateService { Selected = new Language { Key = Guid.NewGuid() } };
        var notificationService = new NotificationService();

        var sut = new Quilt4NotificationService(notificationService, content, languageState);

        await sut.NotifyAsync(NotificationSeverity.Info,
            summaryKey: null, defaultSummary: "Heads up",
            duration: 5000);

        notificationService.Messages.Should().HaveCount(1);
        notificationService.Messages[0].Duration.Should().Be(5000);
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
        private void Unused() { LanguageLoadedEvent?.Invoke(this, null); LanguageChangedEvent?.Invoke(this, null); DeveloperModeEvent?.Invoke(this, null); }
    }
}
