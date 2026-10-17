using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

// Content source indicator: GetContentResultAsync reports where a value actually came from, so a
// consumer can tell real server content apart from a cache hit or a fallback default. The
// distinction already existed inside RemoteContentCallService but was only written to the Debug log.
public class ContentSourceTests
{
    [Fact]
    public async Task Fresh_fetch_reports_server()
    {
        using var listener = StartListener(out var prefix, found: true);
        var service = BuildContentService(prefix);

        var result = await service.GetContentResultAsync("Some.Key", "the-default", Guid.NewGuid(), ContentFormat.String, application: "App");

        result.Source.Should().Be(ContentSource.Server);
        result.Value.Should().Be("from-server");
        result.Stale.Should().BeFalse();
    }

    [Fact]
    public async Task Second_read_of_server_content_reports_cache()
    {
        using var listener = StartListener(out var prefix, found: true);
        var service = BuildContentService(prefix);
        var languageKey = Guid.NewGuid();

        await service.GetContentResultAsync("Some.Key", "the-default", languageKey, ContentFormat.String, application: "App");
        var second = await service.GetContentResultAsync("Some.Key", "the-default", languageKey, ContentFormat.String, application: "App");

        second.Source.Should().Be(ContentSource.Cache);
        second.Value.Should().Be("from-server");
        second.Stale.Should().BeFalse();
    }

    [Fact]
    public async Task Unseeded_key_reports_default()
    {
        using var listener = StartListener(out var prefix, found: false);
        var service = BuildContentService(prefix);

        var result = await service.GetContentResultAsync("Missing.Key", "the-default", Guid.NewGuid(), ContentFormat.String, application: "App");

        result.Source.Should().Be(ContentSource.Default);
        result.Value.Should().Be("the-default");
        result.Stale.Should().BeTrue();
    }

    // The trap this feature exists to avoid. CacheFailure writes the caller's default into the local
    // cache after a 404 so the key isn't re-requested every render. Without provenance stored on the
    // cache entry, that default comes back through the cache branch and would be reported as a
    // genuine cache hit from the second render onwards — lying in exactly the case the indicator is
    // meant to diagnose.
    [Fact]
    public async Task Cached_default_still_reports_default_on_second_read()
    {
        using var listener = StartListener(out var prefix, found: false);
        var service = BuildContentService(prefix);
        var languageKey = Guid.NewGuid();

        await service.GetContentResultAsync("Missing.Key", "the-default", languageKey, ContentFormat.String, application: "App");
        var second = await service.GetContentResultAsync("Missing.Key", "the-default", languageKey, ContentFormat.String, application: "App");

        second.Source.Should().Be(ContentSource.Default,
            "a negative-cache entry holds the caller's default, not server content, so it must never report as Cache");
        second.Value.Should().Be("the-default");
        second.Stale.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_api_key_reports_noapikey()
    {
        using var listener = StartListener(out var prefix, found: true);
        var service = BuildContentService(prefix, o => o.ApiKey = "");

        var result = await service.GetContentResultAsync("Some.Key", "the-default", Guid.NewGuid(), ContentFormat.String, application: "App");

        result.Source.Should().Be(ContentSource.NoApiKey);
        result.Value.Should().Be("the-default");
    }

    [Fact]
    public async Task Developer_language_reports_developer()
    {
        using var listener = StartListener(out var prefix, found: true);
        var service = BuildContentService(prefix);

        var result = await service.GetContentResultAsync("Some.Key", "the-default", Language.DeveloperLanguageKey, ContentFormat.String, application: "App");

        result.Source.Should().Be(ContentSource.Developer);
        result.Value.Should().Be("X");
    }

    [Fact]
    public async Task Key_language_resolves_to_the_key_itself()
    {
        using var listener = StartListener(out var prefix, found: true);
        var service = BuildContentService(prefix);

        var result = await service.GetContentResultAsync("Some.Key", "the-default", Language.KeyLanguageKey, ContentFormat.String, application: "App");

        result.Value.Should().Be("Some.Key");
        result.Source.Should().Be(ContentSource.Developer);
        result.Success.Should().BeTrue();
    }

    // Acceptance: the key language must generate no content traffic. Pointed at a port with nothing
    // listening — a call would fail to connect and fall back to "the-default", so the key coming back
    // is the proof that the short-circuit ran ahead of the cache and the HTTP path.
    [Fact]
    public async Task Key_language_makes_no_http_call()
    {
        var service = BuildContentService($"http://127.0.0.1:{GetFreePort()}/");

        var result = await service.GetContentResultAsync("Some.Key", "the-default", Language.KeyLanguageKey, ContentFormat.String, application: "App");

        result.Value.Should().Be("Some.Key");
        result.Source.Should().Be(ContentSource.Developer);
    }

    // Regression guard: the legacy tuple must keep its exact behaviour. A cached default previously
    // returned Success = true, and consumers may branch on that.
    [Fact]
    public async Task Legacy_tuple_keeps_success_true_for_a_cached_default()
    {
        using var listener = StartListener(out var prefix, found: false);
        var service = BuildContentService(prefix);
        var languageKey = Guid.NewGuid();

        await service.GetContentAsync("Missing.Key", "the-default", languageKey, ContentFormat.String, application: "App");
        var second = await service.GetContentAsync("Missing.Key", "the-default", languageKey, ContentFormat.String, application: "App");

        second.Success.Should().BeTrue("the legacy tuple's Success semantics must not change");
        second.Value.Should().Be("the-default");
    }

    private static IContentService BuildContentService(string baseAddress, Action<ContentOptions> configure = null)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    o.SlowLogThreshold = TimeSpan.Zero;
                    // Keep the negative-cache entry fresh for the duration of a test so the second
                    // read takes the cache branch rather than re-fetching.
                    o.FailureCacheDuration = TimeSpan.FromMinutes(5);
                    configure?.Invoke(o);
                });
                services.AddLogging(b => b.ClearProviders());
            })
            .Build();

        return host.Services.GetRequiredService<IContentService>();
    }

    private static HttpListener StartListener(out string prefix, bool found)
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

                if (!found)
                {
                    ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    ctx.Response.Close();
                    continue;
                }

                var validTo = DateTime.UtcNow.AddMinutes(10).ToString("O");
                var json = $"{{\"value\":\"from-server\",\"validTo\":\"{validTo}\"}}";
                var bytes = Encoding.UTF8.GetBytes(json);
                ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
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
