using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Quilt4Net.Toolkit.Features.Api;
using Quilt4Net.Toolkit.Features.Health;
using Quilt4Net.Toolkit.Features.Probe;
using Xunit;

namespace Quilt4Net.Toolkit.Health.Tests;

// GitHub #232: Timeout must bound every check, and one check's synchronous part must not hold up the others.
public class CheckTimeoutBoundTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(5);

    private readonly Mock<IServiceProvider> _serviceProvider = new(MockBehavior.Loose);
    private readonly Mock<ILogger<HealthService>> _logger = new(MockBehavior.Loose);
    private readonly Mock<IHostEnvironment> _hostEnvironment = new(MockBehavior.Loose);
    private readonly Mock<IHostedServiceProbeRegistry> _hostedServiceProbeRegistry = new(MockBehavior.Loose);

    private HealthService BuildSut(Quilt4NetHealthApiOptions option)
        => new(_hostEnvironment.Object, _serviceProvider.Object, _hostedServiceProbeRegistry.Object, option, _logger.Object, new ComponentCheckCache(TimeProvider.System));

    private static Task<HealthResponse> PollOffThreadAsync(HealthService sut, CancellationToken cancellationToken)
        => Task.Run(async () => (await sut.GetStatusAsync(null, false, cancellationToken).ToArrayAsync(CancellationToken.None)).ToHealthResponse(), CancellationToken.None);

    [Fact]
    public async Task Timeout_bounds_a_cancellation_aware_check_that_ignores_its_token()
    {
        using var release = new ManualResetEventSlim();
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponent(new Component
        {
            Name = "ignores-token",
            Essential = false,
            Timeout = ShortTimeout,
            CheckAsync = _ => Task.FromResult(new CheckResult { Success = true }),
            CheckWithCancellationAsync = async (_, _) =>
            {
                await Task.Run(() => release.Wait(Generous), CancellationToken.None);
                return new CheckResult { Success = true };
            }
        });
        var sut = BuildSut(option);

        try
        {
            var sw = Stopwatch.StartNew();
            var result = await PollOffThreadAsync(sut, TestContext.Current.CancellationToken).WaitAsync(Generous, TestContext.Current.CancellationToken);
            sw.Stop();

            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "Timeout must bound a cancellation-aware check even when it never looks at its token");
            result.Status.Should().Be(HealthStatus.Degraded);
            result.Components.Single().Value.Details["message"].Should().Contain("timed out");
        }
        finally
        {
            release.Set();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Time_spent_in_the_synchronous_part_counts_against_the_timeout(bool cancellationAware)
    {
        using var release = new ManualResetEventSlim();
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponent(new Component
        {
            Name = "blocks-before-await",
            Essential = false,
            Timeout = ShortTimeout,
            CheckAsync = _ =>
            {
                release.Wait(Generous);
                return Task.FromResult(new CheckResult { Success = true });
            },
            CheckWithCancellationAsync = cancellationAware
                ? (_, _) =>
                {
                    release.Wait(Generous);
                    return Task.FromResult(new CheckResult { Success = true });
                }
                : null
        });
        var sut = BuildSut(option);

        try
        {
            var result = await PollOffThreadAsync(sut, TestContext.Current.CancellationToken).WaitAsync(Generous, TestContext.Current.CancellationToken);

            result.Status.Should().Be(HealthStatus.Degraded, "a check that blocks before returning its task must still be cut off at its Timeout");
            result.Components.Single().Value.Details["message"].Should().Contain("timed out");
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task A_check_that_blocks_synchronously_does_not_delay_another_component()
    {
        using var release = new ManualResetEventSlim();
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponent(new Component
        {
            Name = "blocking",
            Essential = false,
            CheckAsync = _ =>
            {
                release.Wait(Generous);
                return Task.FromResult(new CheckResult { Success = true });
            }
        });
        option.AddComponent(new Component
        {
            Name = "fast",
            CheckAsync = _ => Task.FromResult(new CheckResult { Success = true })
        });
        var sut = BuildSut(option);

        try
        {
            var first = Task.Run(async () =>
            {
                await using var enumerator = sut.GetStatusAsync(null, false, TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
                await enumerator.MoveNextAsync();
                return enumerator.Current.Key;
            }, CancellationToken.None);

            var firstName = await first.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

            firstName.Should().Be("fast", "a check that blocks before its first await must not hold up the components after it");
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task A_request_abort_ends_a_check_that_has_no_timeout()
    {
        using var release = new ManualResetEventSlim();
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponent(new Component
        {
            Name = "no-timeout",
            CheckAsync = _ => Task.FromResult(new CheckResult { Success = true }),
            CheckWithCancellationAsync = async (_, _) =>
            {
                await Task.Run(() => release.Wait(Generous), CancellationToken.None);
                return new CheckResult { Success = true };
            }
        });
        var sut = BuildSut(option);
        using var abort = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        try
        {
            var sw = Stopwatch.StartNew();
            var act = () => PollOffThreadAsync(sut, abort.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "an aborted request must not wait for a check that ignores its token");
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task A_request_abort_is_not_cached_as_a_failed_result()
    {
        var runs = 0;
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponent(new Component
        {
            Name = "cached",
            CacheDuration = TimeSpan.FromMinutes(1),
            CheckAsync = _ => Task.FromResult(new CheckResult { Success = true }),
            CheckWithCancellationAsync = async (_, ct) =>
            {
                if (Interlocked.Increment(ref runs) == 1)
                {
                    await Task.Delay(System.Threading.Timeout.Infinite, ct);
                }

                return new CheckResult { Success = true };
            }
        });
        var sut = BuildSut(option);
        using var abort = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var aborted = () => PollOffThreadAsync(sut, abort.Token);
        await aborted.Should().ThrowAsync<OperationCanceledException>();

        var next = await PollOffThreadAsync(sut, TestContext.Current.CancellationToken).WaitAsync(Generous, TestContext.Current.CancellationToken);

        next.Status.Should().Be(HealthStatus.Healthy, "the abort belonged to one caller and must not be served to the next one from the cache");
        runs.Should().Be(2);
    }
}
