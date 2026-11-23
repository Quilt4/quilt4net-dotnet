using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quilt4Net.Toolkit.Features.Health;
using Xunit;

namespace Quilt4Net.Toolkit.Health.Tests;

// GitHub #233, end to end: AddQuilt4NetHealth wires the refresher in, and a probe gets the warmed-up result.
public class BackgroundRefreshEndpointTests : IAsyncLifetime
{
    private WebApplication _app;
    private HttpClient _client;
    private int _runs;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.AddQuilt4NetHealth(o =>
        {
            o.Certificate.SelfCheckEnabled = false;

            o.AddComponent(new Component
            {
                Name = "Mailbox",
                Essential = false,
                CacheDuration = TimeSpan.FromMinutes(5),
                RefreshInBackground = true,
                CheckAsync = _ =>
                {
                    Interlocked.Increment(ref _runs);
                    return Task.FromResult(new CheckResult { Success = true, Message = "signed in" });
                }
            });
        });

        _app = builder.Build();
        _app.UseQuilt4NetHealth();

        await _app.StartAsync();

        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app != null) await _app.DisposeAsync();
    }

    [Fact]
    public void The_refresher_is_registered_as_a_hosted_service()
    {
        _app.Services.GetServices<IHostedService>().Should().ContainSingle(x => x is ComponentRefreshService);
    }

    [Fact]
    public async Task A_probe_gets_the_result_the_refresher_warmed_up_at_start()
    {
        var sw = Stopwatch.StartNew();
        while (Volatile.Read(ref _runs) == 0 && sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        var body = await _client.GetStringAsync("/api/Health", TestContext.Current.CancellationToken);

        Volatile.Read(ref _runs).Should().Be(1, "the warm-up ran the check once and the probe reused its result");
        body.Should().Contain("signed in").And.Contain("checkedAt");
    }
}
