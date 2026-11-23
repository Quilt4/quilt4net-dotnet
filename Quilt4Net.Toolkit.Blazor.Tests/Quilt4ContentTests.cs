using Bunit;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Radzen;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class Quilt4ContentTests : BunitContext
{
    private readonly StubContentService _contentService = new();
    private readonly RecordingLoggerProvider _loggerProvider = new();

    public Quilt4ContentTests()
    {
        // Quilt4Content imports a dynamic data: URL and invokes getInnerHtml on the holder ref.
        // Loose mode satisfies the import; an explicit module stub makes getInnerHtml return a
        // non-empty string so the post-render condition (string.IsNullOrEmpty(_content)) becomes
        // false and OnAfterRenderAsync doesn't loop forever.
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule(_ => true)
            .Setup<string>("getInnerHtml", _ => true)
            .SetResult("Default fallback");

        Services.AddSingleton<IContentService>(_contentService);
        Services.AddSingleton<IEditContentService>(new StubEditContentService());
        Services.AddSingleton<IContentSourceService>(new ContentSourceService());
        Services.AddSingleton<ILanguageStateService>(new StubLanguageState());
        Services.AddScoped<DialogService>();
        Services.AddLogging(b => b.AddProvider(_loggerProvider));
    }

    [Fact]
    public void Render_does_not_throw_when_auto_register_SetContentAsync_fails()
    {
        // Repro for issue #107: a 401 (or any other failure) on the first-render
        // SetContentAsync write used to escape OnAfterRenderAsync and crash the page.
        // The reads-degrade-gracefully contract now applies to the auto-register write too.
        _contentService.SetContentThrow = new HttpRequestException(
            "Response status code does not indicate success: 401 (Unauthorized).");

        var act = () => Render<Quilt4Content>(p => p
            .Add(x => x.Key, "test-key")
            .AddChildContent("Default fallback"));

        act.Should().NotThrow();
    }

    [Fact]
    public void Failed_auto_register_logs_a_warning_naming_the_key()
    {
        _contentService.SetContentThrow = new HttpRequestException("401");

        Render<Quilt4Content>(p => p
            .Add(x => x.Key, "about-page")
            .AddChildContent("Default"));

        _loggerProvider.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("about-page"));
    }

    [Fact]
    public void Successful_auto_register_does_not_log_a_warning()
    {
        // Sanity-check the happy path didn't get noisier: a successful SetContentAsync should
        // still pass through silently (no warning emitted from the new try/catch).
        Render<Quilt4Content>(p => p
            .Add(x => x.Key, "ok-key")
            .AddChildContent("Default"));

        _loggerProvider.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void No_child_content_renders_local_placeholder_and_persists_nothing()
    {
        // Previously a key with no stored value and no ChildContent fetched lorem-ipsum from the
        // external lorem-api.com and auto-registered it as the key's default. It must now render a
        // purely local placeholder and write nothing back to the content store.
        var cut = Render<Quilt4Content>(p => p
            .Add(x => x.Key, "missing-key"));

        cut.Markup.Should().Contain("No content for 'missing-key'.");
        _contentService.SetContentCalls.Should().BeEmpty();
    }

    private sealed class StubContentService : IContentService
    {
        public Exception SetContentThrow { get; set; }
        public List<(string Key, string Value)> SetContentCalls { get; } = new();

        public Task<(string Value, bool Success)> GetContentAsync(string key, string defaultValue,
            Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
            => Task.FromResult<(string, bool)>(("", true));

        public Task SetContentAsync(string key, string value, Guid languageKey,
            ContentFormat contentType, string application = null)
        {
            if (SetContentThrow != null) throw SetContentThrow;
            SetContentCalls.Add((key, value));
            return Task.CompletedTask;
        }

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

        public Language Selected { get; set; } = new() { Name = "Default", Key = Guid.Empty };
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

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Entries);
        public void Dispose() { }

        private sealed class RecordingLogger : ILogger
        {
            private readonly List<(LogLevel, string)> _entries;
            public RecordingLogger(List<(LogLevel, string)> entries) => _entries = entries;
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                Func<TState, Exception, string> formatter)
            {
                lock (_entries) _entries.Add((logLevel, formatter(state, exception)));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
