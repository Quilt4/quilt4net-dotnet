using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Tharga.Cache;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class VersionMatrixCellTests
{
    [Fact]
    public void Cell_record_carries_application_environment_version_and_source()
    {
        var cell = new VersionMatrixCell
        {
            ApplicationName = "florida-server",
            Environment = "Production",
            Version = "1.4.7",
            LastSeen = new DateTime(2026, 5, 6, 14, 0, 0, DateTimeKind.Utc),
            Source = VersionMatrixSource.Startup
        };

        cell.ApplicationName.Should().Be("florida-server");
        cell.Environment.Should().Be("Production");
        cell.Version.Should().Be("1.4.7");
        cell.LastSeen.Kind.Should().Be(DateTimeKind.Utc);
        cell.Source.Should().Be(VersionMatrixSource.Startup);
    }

    [Fact]
    public void VersionMatrixCell_array_cache_type_is_registered()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Quilt4Net:ApplicationInsights:WorkspaceId"] = "ws-id",
                ["Quilt4Net:ApplicationInsights:TenantId"] = "tenant",
                ["Quilt4Net:ApplicationInsights:ClientId"] = "client",
                ["Quilt4Net:ApplicationInsights:ClientSecret"] = "secret"
            })
            .Build();

        services.AddQuilt4NetApplicationInsightsClient(configuration);
        var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<ITimeToLiveCache>();
        cache.Should().NotBeNull();
    }
}
