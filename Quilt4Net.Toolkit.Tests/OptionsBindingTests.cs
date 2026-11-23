using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Guards that AddQuilt4NetContent / AddQuilt4NetRemoteConfiguration bind ALL appsettings fields —
/// not just ApiKey + Quilt4NetAddress. Regression guard for the previous cherry-picking registration
/// that silently ignored HttpTimeout / Ttl / FailureCacheDuration / Application / StaleWhileRevalidate.
/// </summary>
public class OptionsBindingTests
{
    private static IConfiguration Config(Dictionary<string, string> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Content_binds_all_configured_fields_from_appsettings()
    {
        var configuration = Config(new()
        {
            ["Quilt4Net:Content:ApiKey"] = "k",
            ["Quilt4Net:Content:HttpTimeout"] = "00:00:12",
            ["Quilt4Net:Content:FailureCacheDuration"] = "00:05:00",
            ["Quilt4Net:Content:Application"] = "MyApp",
            ["Quilt4Net:Content:StaleWhileRevalidate"] = "false",
        });

        var services = new ServiceCollection();
        services.AddQuilt4NetContent(configuration);
        var o = services.BuildServiceProvider().GetRequiredService<IOptions<ContentOptions>>().Value;

        o.ApiKey.Should().Be("k");
        o.HttpTimeout.Should().Be(TimeSpan.FromSeconds(12));
        o.FailureCacheDuration.Should().Be(TimeSpan.FromMinutes(5));
        o.Application.Should().Be("MyApp");
        o.StaleWhileRevalidate.Should().BeFalse();
    }

    [Fact]
    public void Content_falls_back_to_top_level_address_when_no_subsection_value()
    {
        var configuration = Config(new()
        {
            ["Quilt4Net:ApiKey"] = "top-key",
            ["Quilt4Net:Quilt4NetAddress"] = "https://example.test/",
        });

        var services = new ServiceCollection();
        services.AddQuilt4NetContent(configuration);
        var o = services.BuildServiceProvider().GetRequiredService<IOptions<ContentOptions>>().Value;

        o.ApiKey.Should().Be("top-key", "ApiKey falls back to the top-level Quilt4Net:ApiKey");
        o.Quilt4NetAddress.Should().Be("https://example.test/", "address falls back to the top-level value when no Content subsection");
    }

    [Fact]
    public void RemoteConfiguration_binds_all_configured_fields_from_appsettings()
    {
        var configuration = Config(new()
        {
            ["Quilt4Net:RemoteConfiguration:ApiKey"] = "k",
            ["Quilt4Net:RemoteConfiguration:HttpTimeout"] = "00:00:09",
            ["Quilt4Net:RemoteConfiguration:Ttl"] = "00:10:00",
            ["Quilt4Net:RemoteConfiguration:Application"] = "Svc",
            ["Quilt4Net:RemoteConfiguration:StaleWhileRevalidate"] = "false",
        });

        var services = new ServiceCollection();
        services.AddQuilt4NetRemoteConfiguration(configuration);
        var o = services.BuildServiceProvider().GetRequiredService<IOptions<RemoteConfigurationOptions>>().Value;

        o.ApiKey.Should().Be("k");
        o.HttpTimeout.Should().Be(TimeSpan.FromSeconds(9));
        o.Ttl.Should().Be(TimeSpan.FromMinutes(10));
        o.Application.Should().Be("Svc");
        o.StaleWhileRevalidate.Should().BeFalse();
    }

    [Fact]
    public void Defaults_apply_when_no_configuration_present()
    {
        var services = new ServiceCollection();
        services.AddQuilt4NetContent(Config(new()));
        var o = services.BuildServiceProvider().GetRequiredService<IOptions<ContentOptions>>().Value;

        o.StaleWhileRevalidate.Should().BeTrue("default is preserved when not configured");
        o.HttpTimeout.Should().Be(TimeSpan.FromSeconds(5));
        o.Quilt4NetAddress.Should().Be("https://quilt4net.com/");
    }
}
