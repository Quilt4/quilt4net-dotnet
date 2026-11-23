using System.Net;
using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Health;
using Xunit;

namespace Quilt4Net.Toolkit.Health.Tests;

public class HeadEndpointTests : IAsyncLifetime
{
    private WebApplication _app;
    private HttpClient _client;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.AddQuilt4NetHealth(o =>
        {
            o.Certificate.SelfCheckEnabled = false;

            o.AddComponent(new Component
            {
                Name = "Test",
                Essential = true,
                CheckAsync = _ => Task.FromResult(new CheckResult { Success = true, Message = "OK" })
            });
        });

        _app = builder.Build();
        _app.UseQuilt4NetHealth();

        await _app.StartAsync();

        var address = _app.Urls.First();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app != null) await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("/api/Health")]
    [InlineData("/api/Health/Live")]
    [InlineData("/api/Health/Ready")]
    [InlineData("/api/Health/Health")]
    public async Task Head_Returns_Same_StatusCode_As_Get(string path)
    {
        //Arrange & Act
        var getResponse = await _client.GetAsync(path);
        var headResponse = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));

        //Assert
        headResponse.StatusCode.Should().Be(getResponse.StatusCode, $"HEAD {path} should return the same status code as GET");
    }

    [Theory]
    [InlineData("/api/Health")]
    [InlineData("/api/Health/Live")]
    [InlineData("/api/Health/Ready")]
    [InlineData("/api/Health/Health")]
    public async Task Head_Returns_Empty_Body(string path)
    {
        //Arrange & Act
        var headResponse = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
        var body = await headResponse.Content.ReadAsStringAsync();

        //Assert
        headResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().BeEmpty($"HEAD {path} should not return a body");
    }

    [Theory]
    [InlineData("/api/Health")]
    [InlineData("/api/Health/Live")]
    [InlineData("/api/Health/Ready")]
    [InlineData("/api/Health/Health")]
    public async Task Head_Returns_Status_Header(string path)
    {
        //Arrange & Act
        var getResponse = await _client.GetAsync(path);
        var headResponse = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));

        //Assert
        headResponse.Headers.TryGetValues("Status", out var headValues).Should().BeTrue($"HEAD {path} should include a Status header");
        getResponse.Headers.TryGetValues("Status", out var getValues).Should().BeTrue($"GET {path} should include a Status header");
        headValues.Should().BeEquivalentTo(getValues, $"HEAD and GET {path} should return the same Status header value");
    }

    [Theory]
    [InlineData("/api/Health")]
    [InlineData("/api/Health/Live")]
    [InlineData("/api/Health/Ready")]
    [InlineData("/api/Health/Health")]
    public async Task Get_Returns_NonEmpty_Body(string path)
    {
        //Arrange & Act
        var getResponse = await _client.GetAsync(path);
        var body = await getResponse.Content.ReadAsStringAsync();

        //Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().NotBeEmpty($"GET {path} should return a body");
    }

    [Fact]
    public async Task Head_Version_Is_Disabled()
    {
        //Arrange & Act
        var headResponse = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/api/Health/Version"));

        //Assert
        headResponse.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed, "HEAD for Version is disabled by design");
    }

    [Fact]
    public async Task Head_Metrics_Is_Disabled()
    {
        //Arrange & Act
        var headResponse = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/api/Health/Metrics"));

        //Assert
        headResponse.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed, "HEAD for Metrics is disabled by design");
    }
}
