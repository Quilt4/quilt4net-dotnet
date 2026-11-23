using System.Net.Http.Json;
using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Api;
using Quilt4Net.Toolkit.Features.Health;
using Xunit;

namespace Quilt4Net.Toolkit.Health.Tests;

public class VersionEndpointTests
{
    private static (WebApplication App, HttpClient Client) CreateTestApp(DetailsLevel? detailsLevel = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.AddQuilt4NetHealth(o =>
        {
            o.Certificate.SelfCheckEnabled = false;
            o.IpAddressCheckUri = null;

            if (detailsLevel.HasValue)
            {
                o.Endpoints[HealthEndpoint.Version].Get.Details = detailsLevel.Value;
            }
        });

        var app = builder.Build();
        app.UseQuilt4NetHealth();

        return (app, null!);
    }

    [Fact]
    public async Task Anonymous_With_AuthenticatedOnly_Returns_Version()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.AddQuilt4NetHealth(o =>
        {
            o.Certificate.SelfCheckEnabled = false;
            o.IpAddressCheckUri = null;
            o.Endpoints[HealthEndpoint.Version].Get.Details = DetailsLevel.AuthenticatedOnly;
        });

        var app = builder.Build();
        app.UseQuilt4NetHealth();

        await app.StartAsync();
        try
        {
            var address = app.Urls.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            // Act
            var response = await client.GetFromJsonAsync<VersionResponse>("/api/Health/Version");

            // Assert
            response.Should().NotBeNull();
            response!.Version.Should().NotBeNullOrEmpty("Version should be visible to anonymous users");
            response.Environment.Should().NotBeNullOrEmpty("Environment should be visible to anonymous users");
            response.Machine.Should().BeNull("Machine should be hidden from anonymous users");
            response.IpAddress.Should().BeNull("IpAddress should be hidden from anonymous users");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Anonymous_With_DetailsLevelEveryone_Returns_All_Fields()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.AddQuilt4NetHealth(o =>
        {
            o.Certificate.SelfCheckEnabled = false;
            o.IpAddressCheckUri = null;
            o.Endpoints[HealthEndpoint.Version].Get.Details = DetailsLevel.Everyone;
        });

        var app = builder.Build();
        app.UseQuilt4NetHealth();

        await app.StartAsync();
        try
        {
            var address = app.Urls.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            // Act
            var response = await client.GetFromJsonAsync<VersionResponse>("/api/Health/Version");

            // Assert
            response.Should().NotBeNull();
            response!.Version.Should().NotBeNullOrEmpty("Version should be visible when DetailsLevel is Everyone");
            response.Machine.Should().NotBeNullOrEmpty("Machine should be visible when DetailsLevel is Everyone");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Anonymous_With_DetailsLevelNoOne_Hides_Sensitive_Fields_But_Keeps_Version()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.AddQuilt4NetHealth(o =>
        {
            o.Certificate.SelfCheckEnabled = false;
            o.IpAddressCheckUri = null;
            o.Endpoints[HealthEndpoint.Version].Get.Details = DetailsLevel.NoOne;
        });

        var app = builder.Build();
        app.UseQuilt4NetHealth();

        await app.StartAsync();
        try
        {
            var address = app.Urls.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            // Act
            var response = await client.GetFromJsonAsync<VersionResponse>("/api/Health/Version");

            // Assert
            response.Should().NotBeNull();
            response!.Version.Should().NotBeNullOrEmpty("Version should always be visible, even with DetailsLevel.NoOne");
            response.Environment.Should().NotBeNullOrEmpty("Environment should always be visible");
            response.Machine.Should().BeNull("Machine should be hidden when DetailsLevel is NoOne");
            response.IpAddress.Should().BeNull("IpAddress should be hidden when DetailsLevel is NoOne");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }
}
