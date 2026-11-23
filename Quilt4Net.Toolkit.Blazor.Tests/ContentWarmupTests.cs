using System.Collections.Concurrent;
using Blazored.LocalStorage;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit;
using Quilt4Net.Toolkit.Blazor;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class ContentWarmupTests
{
    [Fact]
    public async Task HostedService_warms_the_default_language_on_start_when_enabled()
    {
        var call = new RecordingRemoteCallService();
        var sut = new ContentWarmupHostedService(call, new RecordingConnectionService(), Options.Create(new ContentOptions { WarmUpEnabled = true }),
            NullLogger<ContentWarmupHostedService>.Instance);

        await sut.StartAsync(CancellationToken.None);

        (await WaitUntil(() => call.Calls.Any(c => c == Guid.Empty))).Should().BeTrue();
        call.Calls.Should().ContainSingle().Which.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task HostedService_warms_the_connection_probe_on_start()
    {
        // #156: the probe is shared process-wide, so paying for it here keeps it off the render
        // path of the first circuit — where a spinner is actually seen.
        var probe = new RecordingConnectionService();
        var sut = new ContentWarmupHostedService(new RecordingRemoteCallService(), probe, Options.Create(new ContentOptions { WarmUpEnabled = true }),
            NullLogger<ContentWarmupHostedService>.Instance);

        await sut.StartAsync(CancellationToken.None);

        (await WaitUntil(() => probe.Probed.Contains(Service.Content))).Should().BeTrue();
    }

    [Fact]
    public async Task A_failing_content_warm_up_does_not_skip_the_probe()
    {
        // The two warm-ups are independent; one failing must not silently cost the other.
        var probe = new RecordingConnectionService();
        var sut = new ContentWarmupHostedService(new ThrowingRemoteCallService(), probe, Options.Create(new ContentOptions { WarmUpEnabled = true }),
            NullLogger<ContentWarmupHostedService>.Instance);

        await sut.StartAsync(CancellationToken.None);

        (await WaitUntil(() => probe.Probed.Contains(Service.Content))).Should().BeTrue();
    }

    [Fact]
    public async Task HostedService_does_not_warm_when_disabled()
    {
        var call = new RecordingRemoteCallService();
        var probe = new RecordingConnectionService();
        var sut = new ContentWarmupHostedService(call, probe, Options.Create(new ContentOptions { WarmUpEnabled = false }),
            NullLogger<ContentWarmupHostedService>.Instance);

        await sut.StartAsync(CancellationToken.None);

        await Task.Delay(150);
        call.Calls.Should().BeEmpty();
        probe.Probed.Should().BeEmpty("WarmUpEnabled turns the whole startup warm-up off");
    }

    [Fact]
    public async Task LanguageStateService_warms_a_newly_selected_non_default_language()
    {
        var call = new RecordingRemoteCallService();
        var other = new Language { Key = Guid.NewGuid(), Name = "Swedish" };
        var languages = new[] { new Language { Key = Guid.Empty, Name = "Default" }, other };
        var sut = new LanguageStateService(new FakeLanguageService(languages), new NoopLocalStorage(), call,
            NullLogger<LanguageStateService>.Instance);

        sut.Selected = other;

        (await WaitUntil(() => call.Calls.Contains(other.Key))).Should().BeTrue("selecting a non-default language warms its content");
        call.Calls.Should().NotContain(Guid.Empty, "the default language is warmed at startup, not by the selector");
    }

    [Fact]
    public async Task Reload_content_clears_the_cache_and_then_re_warms_the_configured_languages()
    {
        // The re-warm is the whole point of "Reload Content": nothing else refills the singleton
        // cache after a clear (the hosted service only runs at startup), so without it the button is
        // a flush and content trickles back one key at a time as pages render.
        var call = new RecordingRemoteCallService();
        var state = NewLanguageState(call);

        await ContentReloader.ReloadAsync(state, new DelegatingContentService(call), call);

        call.Events.Should().ContainInOrder("clear", $"warm:{Guid.Empty}");
    }

    [Fact]
    public async Task Reload_content_survives_a_failing_warm_up()
    {
        // Best-effort: the forced page reload must still happen, falling back to per-key fetching.
        var call = new ThrowingRemoteCallService();
        var state = NewLanguageState(call);

        var act = async () => await ContentReloader.ReloadAsync(state, new DelegatingContentService(call), call);

        await act.Should().NotThrowAsync();
        call.Events.Should().Contain("clear");
    }

    private static LanguageStateService NewLanguageState(IRemoteContentCallService call)
    {
        var languages = new[] { new Language { Key = Guid.Empty, Name = "Default" } };
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

    private class RecordingRemoteCallService : IRemoteContentCallService
    {
        public ConcurrentBag<Guid> Calls { get; } = [];

        /// <summary>Ordered log — Calls is a bag, and "clear before warm" is an ordering claim.</summary>
        public ConcurrentQueue<string> Events { get; } = new();

        public Task WarmCacheAsync(Guid languageKey, string application = null)
        {
            Calls.Add(languageKey);
            Events.Enqueue($"warm:{languageKey}");
            return Task.CompletedTask;
        }

        // Mirrors the real service with no WarmUpLanguages configured: warm the default language.
        public virtual Task WarmConfiguredLanguagesAsync(string application = null) => WarmCacheAsync(Guid.Empty, application);

        public Task<(string Value, bool Success)> GetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
            => Task.FromResult((defaultValue, true));
        public Task<ContentResult> GetContentResultAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
            => Task.FromResult(new ContentResult { Value = defaultValue, Success = true, Source = ContentSource.Default, Stale = false });
        public Task SetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat contentType, string application = null) => Task.CompletedTask;
        public Task<Language[]> GetLanguagesAsync(bool forceReload) => Task.FromResult(Array.Empty<Language>());

        public Task ClearContentCacheAsync()
        {
            Events.Enqueue("clear");
            return Task.CompletedTask;
        }

        public IReadOnlyDictionary<Guid, int> GetCacheCountsByLanguage() => new Dictionary<Guid, int>();
    }

    private sealed class ThrowingRemoteCallService : RecordingRemoteCallService
    {
        public override Task WarmConfiguredLanguagesAsync(string application = null) => throw new HttpRequestException("server unreachable");
    }

    // The real ContentService is a thin pass-through to the call service, so the reload sequence can
    // be observed on one recorder.
    private sealed class DelegatingContentService(IRemoteContentCallService remote) : IContentService
    {
        public Task<(string Value, bool Success)> GetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
            => remote.GetContentAsync(key, defaultValue, languageKey, contentType, application, translations);
        public Task SetContentAsync(string key, string value, Guid languageKey, ContentFormat contentType, string application = null)
            => remote.SetContentAsync(key, value, languageKey, contentType, application);
        public Task ClearCacheAsync() => remote.ClearContentCacheAsync();
    }

    private sealed class RecordingConnectionService : IConnectionService
    {
        public ConcurrentBag<Service> Probed { get; } = [];

        public Task<ConnectionResult> CanConnectAsync(Service service)
        {
            Probed.Add(service);
            return Task.FromResult(new ConnectionResult { Success = true });
        }
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
