using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// What a key shows before the server has answered, when the caller supplied text for the selected
/// language.
/// </summary>
/// <remarks>
/// With Swedish selected and both English (the default) and Swedish (in <c>Translations</c>)
/// hard-coded, a new key used to render in English first. The server already answers in Swedish; the
/// client did not wait for it - the first render waits only <c>MaterializationWait</c> - and every
/// local fallback returned the default alone, ignoring the Swedish it had been handed.
/// </remarks>
public class ContentLanguageFallbackTests
{
    private const string App = "App1";
    private static readonly Guid Swedish = Guid.NewGuid();
    private static readonly Dictionary<string, string> SwedishSupplied = new() { ["Svenska"] = "Hej" };

    [Fact]
    public async Task A_slow_new_key_shows_the_supplied_text_for_the_selected_language()
    {
        using var listener = StartListener(out var prefix, singleKeyDelay: TimeSpan.FromSeconds(2));
        var remote = await BuildAsync(prefix, wait: TimeSpan.FromMilliseconds(100));

        var result = await remote.GetContentResultAsync("new-key", "Hello", Swedish, ContentFormat.String, App, SwedishSupplied);

        result.Value.Should().Be("Hej", "the caller handed us the Swedish text; showing English while the server stores it is the reported bug");
    }

    [Fact]
    public async Task The_language_name_matches_case_insensitively_like_the_server()
    {
        using var listener = StartListener(out var prefix, singleKeyDelay: TimeSpan.FromSeconds(2));
        var remote = await BuildAsync(prefix, wait: TimeSpan.FromMilliseconds(100));

        var result = await remote.GetContentResultAsync("new-key", "Hello", Swedish, ContentFormat.String, App,
            new Dictionary<string, string> { ["svenska"] = "Hej" });

        result.Value.Should().Be("Hej");
    }

    [Fact]
    public async Task A_failing_server_shows_the_supplied_text_and_keeps_showing_it()
    {
        using var listener = StartListener(out var prefix, singleKeyStatus: 500);
        var remote = await BuildAsync(prefix);

        var first = await remote.GetContentResultAsync("new-key", "Hello", Swedish, ContentFormat.String, App, SwedishSupplied);
        var second = await remote.GetContentResultAsync("new-key", "Hello", Swedish, ContentFormat.String, App, SwedishSupplied);

        first.Value.Should().Be("Hej");
        second.Value.Should().Be("Hej", "the negative cache must hold the same text, or the second render flips to English");
    }

    [Fact]
    public async Task Without_text_for_the_selected_language_the_default_is_shown()
    {
        using var listener = StartListener(out var prefix, singleKeyDelay: TimeSpan.FromSeconds(2));
        var remote = await BuildAsync(prefix, wait: TimeSpan.FromMilliseconds(100));

        var result = await remote.GetContentResultAsync("new-key", "Hello", Swedish, ContentFormat.String, App,
            new Dictionary<string, string> { ["Deutsch"] = "Hallo" });

        result.Value.Should().Be("Hello");
    }

    [Fact]
    public async Task The_default_language_shows_the_default_even_when_translations_are_supplied()
    {
        using var listener = StartListener(out var prefix, singleKeyDelay: TimeSpan.FromSeconds(2));
        var remote = await BuildAsync(prefix, wait: TimeSpan.FromMilliseconds(100));

        var result = await remote.GetContentResultAsync("new-key", "Hello", Guid.Empty, ContentFormat.String, App, SwedishSupplied);

        result.Value.Should().Be("Hello");
    }

    [Fact]
    public async Task Before_the_language_list_is_loaded_the_default_is_shown()
    {
        // Deliberate: the name for a language key comes from the list the selector already loaded.
        // Fetching it on this path would add a network call exactly when the network is slow.
        using var listener = StartListener(out var prefix, singleKeyDelay: TimeSpan.FromSeconds(2));
        var remote = await BuildAsync(prefix, wait: TimeSpan.FromMilliseconds(100), loadLanguages: false);

        var result = await remote.GetContentResultAsync("new-key", "Hello", Swedish, ContentFormat.String, App, SwedishSupplied);

        result.Value.Should().Be("Hello");
    }

    [Fact]
    public async Task GetMany_fills_an_unresolved_key_with_the_supplied_text()
    {
        using var listener = StartListener(out var prefix, singleKeyStatus: 500);
        var remote = await BuildAsync(prefix);

        var values = await remote.GetManyContentAsync(
            [new ContentRequest { Key = "new-key", DefaultValue = "Hello", Translations = SwedishSupplied }],
            Swedish, ContentFormat.String, App);

        values["new-key"].Should().Be("Hej");
    }

    private static async Task<IRemoteContentCallService> BuildAsync(string baseAddress, TimeSpan? wait = null, bool loadLanguages = true)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    o.Application = App;
                    o.WarmUpEnabled = false;
                    o.HttpTimeout = TimeSpan.FromSeconds(10);
                    if (wait.HasValue) o.MaterializationWait = wait.Value;
                });
            })
            .Build();

        var remote = host.Services.GetRequiredService<IRemoteContentCallService>();

        // What the language selector does before anything renders in the chosen language.
        if (loadLanguages) await remote.GetLanguagesAsync(forceReload: false);

        return remote;
    }

    private static HttpListener StartListener(out string prefix, int singleKeyStatus = 200, TimeSpan? singleKeyDelay = null)
    {
        var port = GetFreePort();
        prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { return; }

                // A server from before batching answers the batch call 404, and the client then asks key
                // by key - what these tests were written against.
                if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url!.AbsolutePath.EndsWith("/batch", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    continue;
                }

                var segments = ctx.Request.Url!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var validTo = DateTime.UtcNow.AddHours(1).ToString("o");

                string body;
                if (segments.Length >= 2 && segments[1].Equals("Language", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = 200;
                    body = $$"""{"languages":[{"key":"{{Guid.Empty}}","name":"English"},{"key":"{{Swedish}}","name":"Svenska"}],"validTo":"{{validTo}}"}""";
                }
                else if (segments.Length >= 3 && segments[2].Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = 200;
                    body = $$"""{"items":[],"validTo":"{{validTo}}"}""";
                }
                else
                {
                    // The delay stands in for the server storing a key it has never seen.
                    if (singleKeyDelay.HasValue) await Task.Delay(singleKeyDelay.Value);
                    ctx.Response.StatusCode = singleKeyStatus;
                    body = $$"""{"value":"from-server","validTo":"{{validTo}}"}""";
                }

                ctx.Response.ContentType = "application/json";
                var buf = System.Text.Encoding.UTF8.GetBytes(body);
                try
                {
                    await ctx.Response.OutputStream.WriteAsync(buf);
                    ctx.Response.Close();
                }
                catch
                {
                    // A caller that gave up on the bounded wait may already be gone. Not a failure.
                }
            }
        });

        return listener;
    }

    private static int GetFreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
