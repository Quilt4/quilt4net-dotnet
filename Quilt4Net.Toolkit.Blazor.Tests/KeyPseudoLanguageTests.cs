using System.Collections.Concurrent;
using Blazored.LocalStorage;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

// Developer mode offers two pseudo-languages: "X", which resolves every key to the literal "X"
// (is this managed content at all?), and "Key", which resolves every key to its own name (which key
// is it?). Neither exists on the server, so neither may reach it.
public class KeyPseudoLanguageTests
{
    [Fact]
    public async Task Developer_mode_lists_both_pseudo_languages()
    {
        var sut = NewLanguageState(new RecordingRemoteCallService());

        sut.DeveloperMode = true;

        (await WaitUntil(() => sut.Languages.Length == 3)).Should().BeTrue("developer mode adds two languages to the one real one");
        sut.Languages.Should().Contain(x => x.Key == Language.DeveloperLanguageKey && x.Name == "X" && x.Developer);
        sut.Languages.Should().Contain(x => x.Key == Language.KeyLanguageKey && x.Name == "Key" && x.Developer);
    }

    [Fact]
    public async Task Neither_pseudo_language_is_listed_with_developer_mode_off()
    {
        var sut = NewLanguageState(new RecordingRemoteCallService());

        (await WaitUntil(() => sut.Languages.Length == 1)).Should().BeTrue();
        sut.Languages.Should().NotContain(x => x.Developer);
    }

    [Fact]
    public async Task Selecting_the_key_language_does_not_warm_the_cache()
    {
        // A warm-up for a language the server has never heard of is a bulk content call that can only
        // fail — and the reason IsPseudo exists rather than a guard repeated per call site.
        var call = new RecordingRemoteCallService();
        var sut = NewLanguageState(call);
        sut.DeveloperMode = true;
        await WaitUntil(() => sut.Languages.Length == 3);

        sut.Selected = sut.Languages.Single(x => x.Key == Language.KeyLanguageKey);

        // Give a warm-up that should not happen time to happen anyway before asserting it didn't.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        call.Calls.Should().NotContain(Language.KeyLanguageKey);
        call.Calls.Should().NotContain(Language.DeveloperLanguageKey);
    }

    private static LanguageStateService NewLanguageState(IRemoteContentCallService call)
    {
        var languages = new[] { new Language { Key = Guid.NewGuid(), Name = "Swedish", Code = "sv" } };
        return new LanguageStateService(new FakeLanguageService(languages), new NoopLocalStorage(), call,
            NullLogger<LanguageStateService>.Instance);
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    private sealed class RecordingRemoteCallService : IRemoteContentCallService
    {
        public ConcurrentBag<Guid> Calls { get; } = [];

        public Task WarmCacheAsync(Guid languageKey, string application = null)
        {
            Calls.Add(languageKey);
            return Task.CompletedTask;
        }

        public Task WarmConfiguredLanguagesAsync(string application = null) => WarmCacheAsync(Guid.Empty, application);

        public Task<(string Value, bool Success)> GetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
            => Task.FromResult((defaultValue, true));

        public Task<ContentResult> GetContentResultAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
            => Task.FromResult(new ContentResult { Value = defaultValue, Success = true, Source = ContentSource.Default, Stale = false });

        public Task SetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat contentType, string application = null) => Task.CompletedTask;
        public Task<Language[]> GetLanguagesAsync(bool forceReload) => Task.FromResult(Array.Empty<Language>());
        public Task ClearContentCacheAsync() => Task.CompletedTask;
        public IReadOnlyDictionary<Guid, int> GetCacheCountsByLanguage() => new Dictionary<Guid, int>();
    }

    private sealed class FakeLanguageService(Language[] languages) : ILanguageService
    {
        public Task<Language[]> GetLanguagesAsync(bool forceReload) => Task.FromResult(languages);
    }

    // Only the two members LanguageStateService touches are implemented; the rest are unused here.
    private sealed class NoopLocalStorage : ILocalStorageService
    {
        public ValueTask<T> GetItemAsync<T>(string key, CancellationToken cancellationToken = default) => new(default(T));
        public ValueTask SetItemAsync<T>(string key, T data, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ClearAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask<bool> ContainKeyAsync(string key, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask<string> GetItemAsStringAsync(string key, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask<string> KeyAsync(int index, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask<IEnumerable<string>> KeysAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask<int> LengthAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask RemoveItemAsync(string key, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask RemoveItemsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask SetItemAsStringAsync(string key, string data, CancellationToken cancellationToken = default) => throw new NotImplementedException();

#pragma warning disable CS0067
        public event EventHandler<ChangingEventArgs> Changing;
        public event EventHandler<ChangedEventArgs> Changed;
#pragma warning restore CS0067
    }
}
