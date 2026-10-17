using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Issue #174: a failed fetch was cached for a full TTL, so a toggle whose refreshes kept timing out
/// stayed pinned to its fallback indefinitely — two days, in the report. These pin the two halves of
/// the fix: the hold-off after a failure is the configured (short) failure duration rather than the
/// last successful response's lifetime, and it widens per consecutive failure instead of retrying at
/// a fixed rate.
/// </summary>
public class FailureCacheBackoffTests
{
    private static readonly TimeSpan ShortFailure = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan LongEnoughToOutlast = TimeSpan.FromMilliseconds(400);

    [Fact]
    public void Backoff_Doubles_Per_Consecutive_Failure()
    {
        var basis = TimeSpan.FromSeconds(5);
        var max = TimeSpan.FromMinutes(5);

        FailureBackoff.Compute(1, basis, max).Should().Be(TimeSpan.FromSeconds(5));
        FailureBackoff.Compute(2, basis, max).Should().Be(TimeSpan.FromSeconds(10));
        FailureBackoff.Compute(3, basis, max).Should().Be(TimeSpan.FromSeconds(20));
        FailureBackoff.Compute(4, basis, max).Should().Be(TimeSpan.FromSeconds(40));
    }

    [Fact]
    public void Backoff_Stops_At_The_Ceiling()
    {
        var basis = TimeSpan.FromSeconds(5);
        var max = TimeSpan.FromMinutes(5);

        FailureBackoff.Compute(7, basis, max).Should().Be(max, "5s doubled six times is 320s, past the 300s ceiling");
        FailureBackoff.Compute(50, basis, max).Should().Be(max);
        FailureBackoff.Compute(int.MaxValue, basis, max).Should().Be(max, "a long outage must not overflow into a negative or tiny interval");
    }

    [Fact]
    public void Backoff_Resets_To_The_Base_Interval_After_A_Success()
    {
        var sut = new FailureBackoff();
        var basis = TimeSpan.FromSeconds(5);
        var max = TimeSpan.FromMinutes(5);

        sut.Next("k", basis, max);
        sut.Next("k", basis, max).Should().Be(TimeSpan.FromSeconds(10));

        sut.Reset("k");

        sut.Next("k", basis, max).Should().Be(basis,
            "a recovered key must start again at the base interval, not at whatever width the outage had reached");
    }

    [Fact]
    public void Backoff_Is_Per_Key()
    {
        var sut = new FailureBackoff();
        var basis = TimeSpan.FromSeconds(5);
        var max = TimeSpan.FromMinutes(5);

        sut.Next("a", basis, max);
        sut.Next("a", basis, max);

        sut.Next("b", basis, max).Should().Be(basis, "one key's outage must not widen another's first retry");
    }

    [Fact]
    public async Task Content_Failure_Is_Held_For_The_Failure_Duration_Not_The_Last_Successful_Ttl()
    {
        // The shadowing bug: CacheFailure preferred _lastKnownTtl — the *content freshness* interval
        // from the last good response — over the configured failure duration, so the option was dead
        // for any key that had ever succeeded, which is every key in a running app.
        using var listener = StartSwitchableListener(out var prefix, out var mode, out var requestCount);
        var (content, remote) = BuildContentService(prefix, o =>
        {
            o.StaleWhileRevalidate = false;
            o.FailureCacheDuration = ShortFailure;
            o.MaxFailureCacheDuration = ShortFailure;
        });
        var languageKey = Guid.NewGuid();

        // A good response with a ten-minute lifetime — this is what used to be reused as the
        // failure hold. Clearing the cache afterwards drops the value but keeps that recorded TTL.
        mode.Serve("the-server-value", validFor: TimeSpan.FromMinutes(10));
        await content.GetContentAsync("Any.Key", "the-default", languageKey, ContentFormat.String, application: "App");
        await content.ClearCacheAsync();

        mode.Fail(HttpStatusCode.InternalServerError);
        await content.GetContentAsync("Any.Key", "the-default", languageKey, ContentFormat.String, application: "App");
        var afterFailure = requestCount();

        await Task.Delay(LongEnoughToOutlast, TestContext.Current.CancellationToken);
        await content.GetContentAsync("Any.Key", "the-default", languageKey, ContentFormat.String, application: "App");

        requestCount().Should().BeGreaterThan(afterFailure,
            "the failure must be held for the configured 60ms, not for the ten-minute TTL of the last successful response");
        remote.Should().NotBeNull();
    }

    [Fact]
    public async Task Content_404_Is_Still_Held_For_The_Not_Found_Duration()
    {
        // A 404 is an answer, not a fault: the server was reached and said there is no override.
        // Holding it for the short failure interval would re-request every unseeded key on nearly
        // every render — the request flood this feature exists to remove.
        using var listener = StartSwitchableListener(out var prefix, out var mode, out var requestCount);
        var (content, _) = BuildContentService(prefix, o =>
        {
            o.StaleWhileRevalidate = false;
            o.FailureCacheDuration = ShortFailure;
            o.MaxFailureCacheDuration = ShortFailure;
            o.NotFoundCacheDuration = TimeSpan.FromMinutes(10);
        });
        var languageKey = Guid.NewGuid();

        mode.Fail(HttpStatusCode.NotFound);
        await content.GetContentAsync("Unseeded.Key", "the-default", languageKey, ContentFormat.String, application: "App");
        await Task.Delay(LongEnoughToOutlast, TestContext.Current.CancellationToken);
        await content.GetContentAsync("Unseeded.Key", "the-default", languageKey, ContentFormat.String, application: "App");

        requestCount().Should().Be(1,
            "the 404 is held for NotFoundCacheDuration, so a second read well past the failure interval must not call again");
    }

    [Fact]
    public async Task Configuration_Failure_Is_Held_For_The_Configured_Failure_Duration()
    {
        // Configuration had no failure-duration setting at all — a private 10-minute constant, itself
        // shadowed by the last successful TTL. This is the reported symptom: a toggle that cannot
        // converge on the server value while calls keep failing.
        using var listener = StartSwitchableListener(out var prefix, out var mode, out var requestCount);
        var service = BuildToggleService(prefix, o =>
        {
            o.StaleWhileRevalidate = false;
            o.FailureCacheDuration = ShortFailure;
            o.MaxFailureCacheDuration = ShortFailure;
        });

        mode.Fail(HttpStatusCode.InternalServerError);
        await service.GetToggleAsync("AssistantPanel.Enabled", fallback: false);
        var afterFirst = requestCount();

        await Task.Delay(LongEnoughToOutlast, TestContext.Current.CancellationToken);
        await service.GetToggleAsync("AssistantPanel.Enabled", fallback: false);

        requestCount().Should().BeGreaterThan(afterFirst,
            "with a 60ms failure duration the toggle must try again, rather than sitting out a ten-minute negative cache");
    }

    [Fact]
    public async Task Configuration_Recovers_As_Soon_As_The_Server_Does()
    {
        // The acceptance criterion behind the issue: once the server answers again, the toggle stops
        // reporting its fallback.
        using var listener = StartSwitchableListener(out var prefix, out var mode, out _);
        var service = BuildToggleService(prefix, o =>
        {
            o.StaleWhileRevalidate = false;
            o.FailureCacheDuration = ShortFailure;
            o.MaxFailureCacheDuration = ShortFailure;
        });

        mode.Fail(HttpStatusCode.InternalServerError);
        var pinned = await service.GetToggleAsync("AssistantPanel.Enabled", fallback: false);
        pinned.Should().BeFalse("nothing has answered yet, so the caller gets its fallback");

        mode.ServeToggle("True", validFor: TimeSpan.FromMinutes(10));
        await Task.Delay(LongEnoughToOutlast, TestContext.Current.CancellationToken);

        var recovered = await service.GetToggleAsync("AssistantPanel.Enabled", fallback: false);

        recovered.Should().BeTrue("the server is reachable again and says True");
    }

    private static (IContentService Content, IRemoteContentCallService Remote) BuildContentService(string baseAddress, Action<ContentOptions> configure)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetContent(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    o.WarmUpEnabled = false;
                    configure(o);
                });
            })
            .Build();

        return (host.Services.GetRequiredService<IContentService>(), host.Services.GetRequiredService<IRemoteContentCallService>());
    }

    private static IFeatureToggleService BuildToggleService(string baseAddress, Action<RemoteConfigurationOptions> configure)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddQuilt4NetRemoteConfiguration(null, o =>
                {
                    o.Quilt4NetAddress = baseAddress;
                    o.ApiKey = "test-key";
                    configure(o);
                });
            })
            .Build();

        return host.Services.GetRequiredService<IFeatureToggleService>();
    }

    private static HttpListener StartSwitchableListener(out string prefix, out ResponseMode mode, out Func<int> requestCount)
    {
        var port = GetFreePort();
        prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var state = new ResponseMode();
        mode = state;
        var hits = 0;
        requestCount = () => Volatile.Read(ref hits);

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

                Interlocked.Increment(ref hits);
                var (status, body, validFor) = state.Current;
                ctx.Response.StatusCode = (int)status;
                if (body != null)
                {
                    var json = $"{{\"value\":\"{body}\",\"validTo\":\"{DateTime.UtcNow.Add(validFor):O}\"}}";
                    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                }
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

    private sealed class ResponseMode
    {
        private volatile Tuple<HttpStatusCode, string, TimeSpan> _current =
            Tuple.Create(HttpStatusCode.OK, (string)null, TimeSpan.FromMinutes(10));

        public (HttpStatusCode Status, string Body, TimeSpan ValidFor) Current =>
            (_current.Item1, _current.Item2, _current.Item3);

        public void Serve(string body, TimeSpan validFor) =>
            _current = Tuple.Create(HttpStatusCode.OK, body, validFor);

        public void ServeToggle(string value, TimeSpan validFor) =>
            _current = Tuple.Create(HttpStatusCode.OK, value, validFor);

        public void Fail(HttpStatusCode status) =>
            _current = Tuple.Create(status, (string)null, TimeSpan.Zero);
    }
}
