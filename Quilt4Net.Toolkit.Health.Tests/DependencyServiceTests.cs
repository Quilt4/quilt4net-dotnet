using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Api;
using Quilt4Net.Toolkit.Features.Health;
using Quilt4Net.Toolkit.Features.Health.Dependency;
using Xunit;

namespace Quilt4Net.Toolkit.Health.Tests;

public class DependencyServiceTests
{
    private static readonly Uri DependencyUri = new("https://dependency.test/");

    [Theory]
    [InlineData(true, HealthStatus.Healthy, HealthStatus.Healthy)]
    [InlineData(false, HealthStatus.Healthy, HealthStatus.Healthy)]
    [InlineData(true, HealthStatus.Unhealthy, HealthStatus.Unhealthy)]
    [InlineData(false, HealthStatus.Unhealthy, HealthStatus.Degraded)]
    [InlineData(true, HealthStatus.Degraded, HealthStatus.Degraded)]
    public async Task MapsProbeStatusToComponentStatus(bool essential, HealthStatus probeStatus, HealthStatus expected)
    {
        //Arrange
        var probe = new FakeProbe(_ => Response(probeStatus));
        var sut = new DependencyService(probe, OptionsWith(essential), new ManualTimeProvider());

        //Act
        var result = await sut.GetStatusAsync(TestContext.Current.CancellationToken).ToArrayAsync(TestContext.Current.CancellationToken);

        //Assert
        var component = result.Single();
        component.Key.Should().Be("dep");
        component.Value.Status.Should().Be(expected);
        component.Value.Uri.Should().Be(DependencyUri);
    }

    [Fact]
    public async Task RepeatedCallsWithinCacheWindow_ProbeOnce()
    {
        //Arrange
        var probe = new FakeProbe(_ => Response(HealthStatus.Healthy));
        var sut = new DependencyService(probe, OptionsWith(essential: true), new ManualTimeProvider());

        //Act
        for (var i = 0; i < 5; i++)
        {
            _ = await sut.GetStatusAsync(TestContext.Current.CancellationToken).ToArrayAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        probe.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task AfterCacheWindowExpires_ProbesAgain()
    {
        //Arrange
        var probe = new FakeProbe(_ => Response(HealthStatus.Healthy));
        var options = OptionsWith(essential: true);
        options.DependencyProbeCacheTime = TimeSpan.FromSeconds(10);
        var time = new ManualTimeProvider();
        var sut = new DependencyService(probe, options, time);

        //Act
        _ = await sut.GetStatusAsync(TestContext.Current.CancellationToken).ToArrayAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(11));
        _ = await sut.GetStatusAsync(TestContext.Current.CancellationToken).ToArrayAsync(TestContext.Current.CancellationToken);

        //Assert
        probe.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task CacheDisabled_ProbesEveryCall()
    {
        //Arrange
        var probe = new FakeProbe(_ => Response(HealthStatus.Healthy));
        var options = OptionsWith(essential: true);
        options.DependencyProbeCacheTime = TimeSpan.Zero;
        var sut = new DependencyService(probe, options, new ManualTimeProvider());

        //Act
        for (var i = 0; i < 3; i++)
        {
            _ = await sut.GetStatusAsync(TestContext.Current.CancellationToken).ToArrayAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        probe.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task ConcurrentCalls_AreCoalescedToOneProbe()
    {
        //Arrange
        var gate = new TaskCompletionSource();
        var probe = new GatedProbe(gate.Task, () => Response(HealthStatus.Healthy));
        var sut = new DependencyService(probe, OptionsWith(essential: true), new ManualTimeProvider());

        //Act
        var calls = Enumerable.Range(0, 10)
            .Select(_ => sut.GetStatusAsync(TestContext.Current.CancellationToken).ToArrayAsync(TestContext.Current.CancellationToken).AsTask())
            .ToArray();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        gate.SetResult();
        await Task.WhenAll(calls);

        //Assert
        probe.CallCount.Should().Be(1);
    }

    private static Quilt4NetHealthApiOptions OptionsWith(bool essential)
    {
        var options = new Quilt4NetHealthApiOptions();
        options.AddDependency(new Dependency { Name = "dep", Uri = DependencyUri, Essential = essential });
        return options;
    }

    private static HealthResponse Response(HealthStatus status) => new()
    {
        Status = status,
        Components = new Dictionary<string, HealthComponent>
        {
            ["Probe"] = new HealthComponent { Status = status }
        }
    };

    private sealed class FakeProbe : IDependencyProbe
    {
        private readonly Func<Dependency, HealthResponse> _factory;
        private int _callCount;

        public FakeProbe(Func<Dependency, HealthResponse> factory) => _factory = factory;

        public int CallCount => _callCount;

        public Task<HealthResponse> ProbeAsync(Dependency dependency, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(_factory(dependency));
        }
    }

    private sealed class GatedProbe : IDependencyProbe
    {
        private readonly Task _gate;
        private readonly Func<HealthResponse> _factory;
        private int _callCount;

        public GatedProbe(Task gate, Func<HealthResponse> factory)
        {
            _gate = gate;
            _factory = factory;
        }

        public int CallCount => _callCount;

        public async Task<HealthResponse> ProbeAsync(Dependency dependency, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            await _gate;
            return _factory();
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
