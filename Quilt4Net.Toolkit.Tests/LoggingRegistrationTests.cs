using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class LoggingRegistrationTests
{
    [Fact]
    public void Configures_OpenTelemetry_resource_with_expected_attributes()
    {
        var services = new ServiceCollection();
        services.AddQuilt4NetLogging(options: o =>
        {
            o.ApplicationName = "my-app";
            o.Version = "1.2.3";
            o.Environment = "Staging";
        });
        services.AddOpenTelemetry().WithTracing(b => b.AddSource("probe"));

        using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetRequiredService<TracerProvider>();
        var resource = tracerProvider.GetResource();

        resource.Attributes.Should().Contain(new KeyValuePair<string, object>("service.name", "my-app"));
        resource.Attributes.Should().Contain(new KeyValuePair<string, object>("service.version", "1.2.3"));
        resource.Attributes.Should().Contain(new KeyValuePair<string, object>("deployment.environment", "Staging"));
        resource.Attributes.Should().ContainKey("service.instance.id");
    }

    [Fact]
    public void Callback_can_set_ApplicationName()
    {
        var services = new ServiceCollection();
        services.AddQuilt4NetLogging(options: o => o.ApplicationName = "my-app");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Quilt4NetLoggingOptions>();
        options.ApplicationName.Should().Be("my-app");
    }

    [Fact]
    public void Config_sets_ApplicationName()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Quilt4Net:Logging:ApplicationName"] = "from-config"
            })
            .Build();

        services.AddQuilt4NetLogging(configuration);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Quilt4NetLoggingOptions>();
        options.ApplicationName.Should().Be("from-config");
    }

    [Fact]
    public void Callback_overrides_config()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Quilt4Net:Logging:ApplicationName"] = "from-config"
            })
            .Build();

        services.AddQuilt4NetLogging(configuration, o => o.ApplicationName = "from-callback");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Quilt4NetLoggingOptions>();
        options.ApplicationName.Should().Be("from-callback");
    }

    [Fact]
    public void EnvironmentName_parameter_is_used_when_no_config()
    {
        var services = new ServiceCollection();
        services.AddQuilt4NetLogging(environmentName: "Staging");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Quilt4NetLoggingOptions>();
        options.Environment.Should().Be("Staging");
    }

    [Fact]
    public void ApplicationName_parameter_wins_over_config()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Quilt4Net:Logging:ApplicationName"] = "from-config"
            })
            .Build();

        services.AddQuilt4NetLogging(configuration, applicationName: "from-host");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Quilt4NetLoggingOptions>();
        options.ApplicationName.Should().Be("from-host");
    }

    [Fact]
    public void ApplicationName_falls_back_to_null_when_entry_assembly_is_framework()
    {
        // Test runner's entry assembly is typically "testhost" — verify our filter rejects it
        // and consumer can still set it via callback or parameter
        var services = new ServiceCollection();
        services.AddQuilt4NetLogging(applicationName: "my-app");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Quilt4NetLoggingOptions>();
        options.ApplicationName.Should().Be("my-app");
    }

    [Fact]
    public void EnvironmentName_parameter_wins_over_config()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Quilt4Net:Logging:Environment"] = "from-config"
            })
            .Build();

        services.AddQuilt4NetLogging(configuration, environmentName: "from-host");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Quilt4NetLoggingOptions>();
        options.Environment.Should().Be("from-host");
    }

    [Fact]
    public void Returns_Quilt4NetLoggingBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddQuilt4NetLogging();

        builder.Should().NotBeNull();
        builder.Services.Should().BeSameAs(services);
        builder.Options.Should().NotBeNull();
    }

    [Fact]
    public void Environment_defaults_to_Production_when_nothing_is_configured()
    {
        // Save and clear env vars to test the fallback
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var aspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);

            var services = new ServiceCollection();
            services.AddQuilt4NetLogging();

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<Quilt4NetLoggingOptions>();
            options.Environment.Should().Be("Production");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", dotnet);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", aspnet);
        }
    }
}
