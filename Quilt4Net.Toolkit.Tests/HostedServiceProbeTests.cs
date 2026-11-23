using System.Reflection;
using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Health;
using Quilt4Net.Toolkit.Features.Probe;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class HostedServiceProbeTests
{
    [Fact]
    public void GetHealth_After_Many_Pulses_Keeps_Window_Bounded_And_PulseCount_Total()
    {
        // Issue #130: pulse history was an unbounded List<long> (one entry per Pulse, never trimmed)
        // and GetHealth() rebuilt a full intervals list over the whole history on every call, eventually
        // throwing OutOfMemoryException. History must now be a bounded rolling window while the reported
        // pulseCount stays the true total.
        var registry = new HostedServiceProbeRegistry();
        var probe = new HostedServiceProbe(registry);
        probe.Register("test", plannedInterval: null, autoMaxInterval: true, pulseWindowSize: 100);

        const int pulses = 200_000;
        for (var i = 0; i < pulses; i++)
        {
            probe.Pulse();
        }

        // GetHealth must not throw, and pulseCount reflects the TOTAL, not the retained window.
        var health = probe.GetHealth();
        health.Details["pulseCount"].Should().Be(pulses.ToString());

        // The retained buffer is bounded by the window size no matter how many pulses arrived.
        GetPulseBufferCount(probe).Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public async Task Pulse_And_GetHealth_Run_Concurrently_Without_Throwing()
    {
        // Issue #130 (secondary): GetHealth() enumerated _pulseTimes on the health thread while
        // Pulse() appended on the hosted-service thread with no synchronization, which could throw
        // (concurrent enumerate/add). Access is now guarded by a lock — this exercises that.
        //
        // Hardened against a CI flake: the pulser carries NO Task.Run cancellation token (so it can
        // only ever RanToCompletion, never Canceled), and the teardown is a plain `await pulser` once
        // the stop flag is set. The previous version passed cts.Token to Task.Run and waited with
        // WaitAsync(timeout, TestContext.Current.CancellationToken); under CI load that wait could
        // surface a spurious TaskCanceledException unrelated to the concurrency being tested. The only
        // thing asserted is that concurrent Pulse()/GetHealth() never throw.
        var registry = new HostedServiceProbeRegistry();
        var probe = new HostedServiceProbe(registry);
        probe.Register("test", plannedInterval: null, autoMaxInterval: true, pulseWindowSize: 50);

        using var cts = new CancellationTokenSource();
        // Deliberately no cancellation token on Task.Run (hence the xUnit1051 suppression): passing one
        // would let a canceled token mark the pulser task Canceled and reintroduce the exact
        // TaskCanceledException flake this test removes. The loop exits purely on the flag.
#pragma warning disable xUnit1051
        var pulser = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                probe.Pulse();
            }
        });
#pragma warning restore xUnit1051

        Action poll = () =>
        {
            for (var i = 0; i < 20_000; i++)
            {
                _ = probe.GetHealth();
            }
        };

        poll.Should().NotThrow("Pulse() and GetHealth() must be safe to call concurrently");

        cts.Cancel();
        await pulser;
    }

    [Fact]
    public void GetHealth_With_Regular_Pulses_Preserves_Detail_Shape()
    {
        var registry = new HostedServiceProbeRegistry();
        var probe = new HostedServiceProbe(registry);
        probe.Register("test", plannedInterval: null, autoMaxInterval: false, pulseWindowSize: 100);

        for (var i = 0; i < 10; i++)
        {
            probe.Pulse();
            Thread.Sleep(5);
        }

        var health = probe.GetHealth();

        health.Status.Should().BeOneOf(HealthStatus.Healthy, HealthStatus.Degraded, HealthStatus.Unhealthy);
        health.Details.Should().ContainKeys(
            "message", "averageFrequency", "averageInterval", "maxInterval",
            "standardDeviation", "lastPulse", "nextExpectedPuse", "pulseCount");
        health.Details["pulseCount"].Should().Be("10");
    }

    [Fact]
    public void Register_Clamps_Window_To_At_Least_Two()
    {
        var registry = new HostedServiceProbeRegistry();
        var probe = new HostedServiceProbe(registry);
        probe.Register("test", plannedInterval: null, autoMaxInterval: true, pulseWindowSize: 0);

        for (var i = 0; i < 10; i++)
        {
            probe.Pulse();
        }

        // Clamped to >= 2 so there are always enough timestamps to form an interval
        // (a window of 0/1 would leave GetHealth permanently in the pre-health branch).
        GetPulseBufferCount(probe).Should().BeGreaterThanOrEqualTo(2);
        probe.GetHealth().Details.Should().ContainKey("averageInterval");
    }

    private static int GetPulseBufferCount(HostedServiceProbe probe)
    {
        var field = typeof(HostedServiceProbe).GetField("_pulseTimes", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var queue = (Queue<long>)field.GetValue(probe)!;
        return queue.Count;
    }
}
