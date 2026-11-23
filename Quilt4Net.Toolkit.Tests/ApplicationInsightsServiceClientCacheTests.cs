using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Tharga.Cache;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class ApplicationInsightsServiceClientCacheTests
{
    [Fact]
    public void Same_credentials_share_one_client_instance()
    {
        var sut = CreateSut();

        var ctx = MakeContext("tenant-A", "client-A");

        var first = sut.GetClient(ctx);
        var second = sut.GetClient(ctx);

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Different_TenantId_returns_distinct_client()
    {
        var sut = CreateSut();

        var a = sut.GetClient(MakeContext("tenant-A", "client-A"));
        var b = sut.GetClient(MakeContext("tenant-B", "client-A"));

        b.Should().NotBeSameAs(a);
    }

    [Fact]
    public void Different_ClientId_returns_distinct_client()
    {
        var sut = CreateSut();

        var a = sut.GetClient(MakeContext("tenant-A", "client-A"));
        var b = sut.GetClient(MakeContext("tenant-A", "client-B"));

        b.Should().NotBeSameAs(a);
    }

    [Fact]
    public void Different_AuthMode_returns_distinct_client()
    {
        var sut = CreateSut();

        var clientSecretCtx = MakeContext("tenant-A", "client-A", ApplicationInsightsAuthMode.ClientSecret);
        var managedIdentityCtx = MakeContext("tenant-A", "client-A", ApplicationInsightsAuthMode.ManagedIdentity);

        var a = sut.GetClient(clientSecretCtx);
        var b = sut.GetClient(managedIdentityCtx);

        b.Should().NotBeSameAs(a);
    }

    private static ApplicationInsightsService CreateSut()
    {
        var options = Options.Create(new ApplicationInsightsOptions
        {
            TenantId = "fallback-tenant",
            WorkspaceId = "fallback-workspace",
            ClientId = "fallback-client",
            ClientSecret = "fallback-secret"
        });

        return new ApplicationInsightsService(
            Mock.Of<ITimeToLiveCache>(),
            new LogCubeDayCache(TimeSpan.FromMinutes(5), () => DateTimeOffset.UtcNow),
            options,
            NullLogger<ApplicationInsightsService>.Instance);
    }

    private static IApplicationInsightsContext MakeContext(string tenantId, string clientId, ApplicationInsightsAuthMode authMode = ApplicationInsightsAuthMode.ClientSecret)
    {
        return new TestContext
        {
            TenantId = tenantId,
            ClientId = clientId,
            WorkspaceId = "ws-" + tenantId,
            ClientSecret = "secret",
            AuthMode = authMode
        };
    }

    private sealed record TestContext : IApplicationInsightsContext
    {
        public string TenantId { get; init; }
        public string WorkspaceId { get; init; }
        public string ClientId { get; init; }
        public string ClientSecret { get; init; }
        public ApplicationInsightsAuthMode AuthMode { get; init; }
    }
}
