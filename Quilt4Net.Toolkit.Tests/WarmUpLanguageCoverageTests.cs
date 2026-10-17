using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quilt4Net.Toolkit.Features.Content;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Issue #163: the periodic re-warm only helps a language it actually covers. A language the user
/// selects at runtime is warmed once by <c>LanguageStateService</c> and is in no configured list, so
/// a re-warm that only walked <see cref="ContentOptions.WarmUpLanguages"/> would leave exactly that
/// language expiring into the per-key fan-out — the reported fault, one language narrower.
/// </summary>
public class WarmUpLanguageCoverageTests
{
    [Fact]
    public async Task A_language_warmed_at_runtime_is_covered_by_later_warm_ups()
    {
        using var listener = StartBulkListener(out var prefix, out var warmedLanguages);
        var remote = BuildRemoteService(prefix);
        var selectedAtRuntime = Guid.NewGuid();

        // What LanguageStateService does when the user switches language.
        await remote.WarmCacheAsync(selectedAtRuntime, application: "App");
        warmedLanguages.Clear();

        // What the periodic re-warm does on every tick.
        await remote.WarmConfiguredLanguagesAsync(application: "App");

        warmedLanguages.Should().Contain(Guid.Empty, "the default language is always warmed");
        warmedLanguages.Should().Contain(selectedAtRuntime,
            "a language somebody is actually reading must keep being re-warmed, not just the configured ones");
    }

    [Fact]
    public async Task A_language_is_not_warmed_twice_in_one_pass()
    {
        using var listener = StartBulkListener(out var prefix, out var warmedLanguages);
        var remote = BuildRemoteService(prefix);

        await remote.WarmCacheAsync(Guid.Empty, application: "App");
        warmedLanguages.Clear();

        await remote.WarmConfiguredLanguagesAsync(application: "App");

        warmedLanguages.Count(x => x == Guid.Empty).Should().Be(1,
            "the default language is both the always-warmed one and a previously-warmed one — it must not be fetched twice");
    }

    private static IRemoteContentCallService BuildRemoteService(string baseAddress)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    o.WarmUpEnabled = false;
                });
            })
            .Build();

        return host.Services.GetRequiredService<IRemoteContentCallService>();
    }

    private static HttpListener StartBulkListener(out string prefix, out ConcurrentBag<Guid> warmedLanguages)
    {
        var port = GetFreePort();
        prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var warmed = new ConcurrentBag<Guid>();
        warmedLanguages = warmed;

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

                // Api/Content/all/{application}/{environment}/{languageKey}
                var segments = ctx.Request.Url?.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
                if (segments.Length > 0 && Guid.TryParse(segments[^1], out var languageKey)) warmed.Add(languageKey);

                var json = $"{{\"items\":[],\"validTo\":\"{DateTime.UtcNow.AddMinutes(10):O}\"}}";
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
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
