using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Features.HeartbeatReporting;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Heartbeat reporting is registered in code but can be switched off in configuration, so one
/// machine or environment stops counting as a live instance without a code change.
/// </summary>
public class HeartbeatReportingRegistrationTests
{
    [Fact]
    public void Reporting_is_on_by_default()
    {
        var services = Register(new Dictionary<string, string>());

        services.Should().Contain(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(HeartbeatReporter));
    }

    [Fact]
    public void Enabled_false_in_configuration_stops_the_reporter()
    {
        var services = Register(new Dictionary<string, string> { ["Quilt4Net:HeartbeatReporting:Enabled"] = "false" });

        services.Should().NotContain(x => x.ImplementationType == typeof(HeartbeatReporter));
        services.BuildServiceProvider().GetRequiredService<IOptions<HeartbeatReportingOptions>>().Value.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Capabilities_and_interval_come_from_configuration()
    {
        var services = Register(new Dictionary<string, string>
        {
            ["Quilt4Net:HeartbeatReporting:Capabilities:0"] = "import",
            ["Quilt4Net:HeartbeatReporting:Capabilities:1"] = "export",
            ["Quilt4Net:HeartbeatReporting:Interval"] = "00:01:30"
        });

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<HeartbeatReportingOptions>>().Value;
        options.Capabilities.Should().Equal("import", "export");
        options.Interval.Should().Be(TimeSpan.FromSeconds(90));
    }

    private static IServiceCollection Register(Dictionary<string, string> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuilt4NetHeartbeatReporting(configuration);
        return services;
    }
}
