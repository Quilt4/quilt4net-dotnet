using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Features.Health;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class HealthRegistrationTests
{
    [Fact]
    public void Callback_can_set_HealthAddress_when_config_is_missing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddQuilt4NetHealthClient(configuration, o => o.HealthAddress = "https://example.com/health");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthClientOptions>>();
        options.Value.HealthAddress.Should().Be("https://example.com/health");
    }

    [Fact]
    public void Callback_can_override_HealthAddress_from_config()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Quilt4Net:HealthClient:HealthAddress"] = "https://from-config.com/health"
            })
            .Build();

        services.AddQuilt4NetHealthClient(configuration, o => o.HealthAddress = "https://from-callback.com/health");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthClientOptions>>();
        options.Value.HealthAddress.Should().Be("https://from-callback.com/health");
    }

    [Fact]
    public void Throws_when_neither_config_nor_callback_provides_address()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var act = () => services.AddQuilt4NetHealthClient(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HealthClient*");
    }

    [Fact]
    public void Uses_config_value_when_no_callback_is_provided()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Quilt4Net:HealthClient:HealthAddress"] = "https://from-config.com/health"
            })
            .Build();

        services.AddQuilt4NetHealthClient(configuration);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthClientOptions>>();
        options.Value.HealthAddress.Should().Be("https://from-config.com/health");
    }
}
