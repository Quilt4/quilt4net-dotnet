using System.Text.Json;
using AwesomeAssertions;
using Moq;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Quilt4Net.Toolkit.Mcp;
using Tharga.Mcp;
using Xunit;

namespace Quilt4Net.Toolkit.Mcp.Tests;

public class ApplicationInsightsResourceProviderTests
{
    [Fact]
    public void Provider_runs_on_System_scope()
    {
        var sut = NewSut();
        sut.Scope.Should().Be(McpScope.System);
    }

    [Fact]
    public async Task Lists_two_resources()
    {
        var sut = NewSut();
        var resources = await sut.ListResourcesAsync(StubContext, default);

        resources.Select(r => r.Uri).Should().BeEquivalentTo(
            ApplicationInsightsResourceProvider.EnvironmentsUri,
            ApplicationInsightsResourceProvider.SummariesUri);
    }

    [Fact]
    public async Task Reading_environments_resource_calls_service_and_serialises_payload()
    {
        var ais = new Mock<IApplicationInsightsService>();
        ais.Setup(x => x.GetEnvironments(It.IsAny<IApplicationInsightsContext>()))
           .Returns(ToAsync(new EnvironmentOption("Production"), new EnvironmentOption("Test")));

        var sut = new ApplicationInsightsResourceProvider(ais.Object, new Quilt4NetMcpOptions());

        var content = await sut.ReadResourceAsync(
            ApplicationInsightsResourceProvider.EnvironmentsUri, StubContext, default);

        content.MimeType.Should().Be("application/json");
        var payload = JsonDocument.Parse(content.Text).RootElement;
        payload.GetProperty("environments").EnumerateArray()
            .Select(e => e.GetProperty("value").GetString())
            .Should().Contain(["Production", "Test"]);
    }

    [Fact]
    public async Task Reading_unknown_uri_throws()
    {
        var sut = NewSut();
        var act = () => sut.ReadResourceAsync("quilt4net://nope", StubContext, default);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static ApplicationInsightsResourceProvider NewSut()
        => new(Mock.Of<IApplicationInsightsService>(), new Quilt4NetMcpOptions());

    private static IMcpContext StubContext => new TestMcpContext();

#pragma warning disable CS1998
    private static async IAsyncEnumerable<T> ToAsync<T>(params T[] items)
#pragma warning restore CS1998
    {
        foreach (var item in items) yield return item;
    }

    private sealed class TestMcpContext : IMcpContext
    {
        public string UserId => "test-user";
        public string TeamId => null;
        public bool IsDeveloper => true;
        public McpScope Scope => McpScope.System;
    }
}
