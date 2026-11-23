using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Quilt4Net.Toolkit.Mcp;
using Tharga.Mcp;
using Xunit;

namespace Quilt4Net.Toolkit.Mcp.Tests;

public class ThargaMcpBuilderExtensionsTests
{
    [Fact]
    public void AddQuilt4Net_registers_options_and_providers()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IApplicationInsightsService>());

        services.AddThargaMcp(mcp =>
        {
            mcp.AddQuilt4Net(o =>
            {
                o.DataAccess = DataAccessLevel.DataRead;
                o.DefaultLookback = TimeSpan.FromHours(2);
            });
        });

        var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<Quilt4NetMcpOptions>();
        options.DataAccess.Should().Be(DataAccessLevel.DataRead);
        options.DefaultLookback.Should().Be(TimeSpan.FromHours(2));

        var toolProviders = sp.GetServices<IMcpToolProvider>().ToArray();
        toolProviders.Should().ContainSingle(p => p is ApplicationInsightsToolProvider);

        var resourceProviders = sp.GetServices<IMcpResourceProvider>().ToArray();
        resourceProviders.Should().ContainSingle(p => p is ApplicationInsightsResourceProvider);
    }

    [Fact]
    public void AddQuilt4Net_uses_defaults_when_no_callback_provided()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IApplicationInsightsService>());

        services.AddThargaMcp(mcp => mcp.AddQuilt4Net());

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<Quilt4NetMcpOptions>();

        options.DataAccess.Should().Be(DataAccessLevel.Metadata);
        options.DefaultLookback.Should().Be(TimeSpan.FromDays(1));
        options.MaxLookback.Should().Be(TimeSpan.FromDays(7));
    }
}
