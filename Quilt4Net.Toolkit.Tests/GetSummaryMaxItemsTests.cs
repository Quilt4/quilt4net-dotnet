using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Tharga.Cache;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Pins the upper-bound contract on <see cref="ApplicationInsightsService.GetSummary"/>.
/// Without a row limit, a fingerprint with 100k+ matching rows over the lookback window
/// returns the whole set, which made detail/summary navigation hang in production.
/// </summary>
public class GetSummaryMaxItemsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task GetSummary_throws_for_non_positive_maxItems(int maxItems)
    {
        var sut = CreateSut(out _);

        var act = () => sut.GetSummary(MakeContext(), "fp", LogSource.Exception, "Production", TimeSpan.FromDays(1), maxItems);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .Where(e => e.ParamName == "maxItems");
    }

    [Fact]
    public async Task GetSummary_includes_maxItems_in_cache_key_so_different_limits_do_not_collide()
    {
        var sut = CreateSut(out var capturedKeys);

        // Both calls will trip the mocked cache (which returns null) but we don't care —
        // we're verifying the keys the service computes before the factory runs.
        try { await sut.GetSummary(MakeContext(), "fp", LogSource.Exception, "Production", TimeSpan.FromDays(1), 100); } catch { }
        try { await sut.GetSummary(MakeContext(), "fp", LogSource.Exception, "Production", TimeSpan.FromDays(1), 500); } catch { }

        capturedKeys.Should().HaveCount(2);
        capturedKeys[0].Should().NotBe(capturedKeys[1]);
        capturedKeys[0].Should().EndWith("|100");
        capturedKeys[1].Should().EndWith("|500");
    }

    [Fact]
    public async Task GetSummary_default_maxItems_is_100()
    {
        var sut = CreateSut(out var capturedKeys);

        try { await sut.GetSummary(MakeContext(), "fp", LogSource.Exception, "Production", TimeSpan.FromDays(1)); } catch { }

        capturedKeys.Should().ContainSingle().Which.Should().EndWith("|100");
    }

    private static ApplicationInsightsService CreateSut(out List<string> capturedKeys)
    {
        var keys = new List<string>();
        capturedKeys = keys;

        var cache = new Mock<ITimeToLiveCache>();
        cache
            .Setup(c => c.GetAsync(It.IsAny<Key>(), It.IsAny<Func<Task<SummaryData>>>()))
            .Callback<Key, Func<Task<SummaryData>>>((key, _) => keys.Add(key.ToString()))
            .Returns(Task.FromResult<SummaryData>(null!));

        var options = Options.Create(new ApplicationInsightsOptions
        {
            TenantId = "tenant",
            WorkspaceId = "workspace",
            ClientId = "client",
            ClientSecret = "secret"
        });

        return new ApplicationInsightsService(
            cache.Object,
            new LogCubeDayCache(TimeSpan.FromMinutes(5), () => DateTimeOffset.UtcNow),
            options,
            NullLogger<ApplicationInsightsService>.Instance);
    }

    private static IApplicationInsightsContext MakeContext() => new TestContext
    {
        TenantId = "tenant",
        WorkspaceId = "workspace",
        ClientId = "client",
        ClientSecret = "secret",
        AuthMode = ApplicationInsightsAuthMode.ClientSecret
    };

    private sealed record TestContext : IApplicationInsightsContext
    {
        public string TenantId { get; init; }
        public string WorkspaceId { get; init; }
        public string ClientId { get; init; }
        public string ClientSecret { get; init; }
        public ApplicationInsightsAuthMode AuthMode { get; init; }
    }
}
