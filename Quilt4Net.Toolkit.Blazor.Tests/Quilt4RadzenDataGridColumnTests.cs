using Bunit;
using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class Quilt4RadzenDataGridColumnTests : BunitContext
{
    private readonly FakeContentService _contentService = new();
    private readonly FakeLanguageStateService _languageStateService = new();

    public Quilt4RadzenDataGridColumnTests()
    {
        Services.AddSingleton<IContentService>(_contentService);
        Services.AddSingleton<ILanguageStateService>(_languageStateService);
        _languageStateService.Selected = new Language { Key = Guid.NewGuid(), Name = "English" };
    }

    [Fact]
    public void Loads_Title_From_Content_Service()
    {
        _contentService.SetResult("Customer Name");

        var cut = Render<Quilt4RadzenDataGridColumn<string>>(parameters => parameters
            .Add(p => p.TitleKey, "col.customer.name")
            .Add(p => p.DefaultTitle, "Name"));

        cut.Instance.LoadedTitle.Should().Be("Customer Name");
    }

    [Fact]
    public void Falls_Back_To_Default_When_Content_Not_Found()
    {
        _contentService.SetResult(null, success: false);

        var cut = Render<Quilt4RadzenDataGridColumn<string>>(parameters => parameters
            .Add(p => p.TitleKey, "col.missing")
            .Add(p => p.DefaultTitle, "Fallback Column"));

        cut.Instance.LoadedTitle.Should().Be("Fallback Column");
    }

    [Fact]
    public void Updates_Title_On_Language_Change()
    {
        _contentService.SetResult("Order Date");

        var cut = Render<Quilt4RadzenDataGridColumn<string>>(parameters => parameters
            .Add(p => p.TitleKey, "col.order.date")
            .Add(p => p.DefaultTitle, "Date"));

        cut.Instance.LoadedTitle.Should().Be("Order Date");

        _contentService.SetResult("Orderdatum");
        _languageStateService.RaiseLanguageChanged();

        cut.WaitForState(() => cut.Instance.LoadedTitle == "Orderdatum");
        cut.Instance.LoadedTitle.Should().Be("Orderdatum");
    }

    [Fact]
    public void Uses_Default_When_Content_Returns_Empty()
    {
        _contentService.SetResult("", success: true);

        var cut = Render<Quilt4RadzenDataGridColumn<string>>(parameters => parameters
            .Add(p => p.TitleKey, "col.empty")
            .Add(p => p.DefaultTitle, "Default Column"));

        cut.Instance.LoadedTitle.Should().Be("Default Column");
    }

    private class FakeContentService : IContentService
    {
        private string _value = "";
        private bool _success = true;

        public void SetResult(string value, bool success = true)
        {
            _value = value;
            _success = success;
        }

        public Task<(string Value, bool Success)> GetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
        {
            if (_success && !string.IsNullOrEmpty(_value))
                return Task.FromResult((_value, true));
            return Task.FromResult((defaultValue ?? "", _success));
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
