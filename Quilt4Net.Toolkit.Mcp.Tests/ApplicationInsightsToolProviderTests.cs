using System.Text.Json;
using AwesomeAssertions;
using Moq;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Quilt4Net.Toolkit.Mcp;
using Tharga.Mcp;
using Xunit;

namespace Quilt4Net.Toolkit.Mcp.Tests;

public class ApplicationInsightsToolProviderTests
{
    private static readonly JsonElement EmptyArgs = JsonDocument.Parse("{}").RootElement;

    [Fact]
    public void Provider_runs_on_System_scope()
    {
        var sut = NewSut(new Quilt4NetMcpOptions());
        sut.Scope.Should().Be(McpScope.System);
    }

    [Fact]
    public async Task Metadata_only_lists_get_environments()
    {
        var sut = NewSut(new Quilt4NetMcpOptions { DataAccess = DataAccessLevel.Metadata });

        var tools = await sut.ListToolsAsync(StubContext, default);

        tools.Select(t => t.Name).Should().BeEquivalentTo(
            ApplicationInsightsToolProvider.GetEnvironmentsToolName);
    }

    [Fact]
    public async Task DataRead_lists_all_seven_tools()
    {
        var sut = NewSut(new Quilt4NetMcpOptions { DataAccess = DataAccessLevel.DataRead });

        var tools = await sut.ListToolsAsync(StubContext, default);

        tools.Select(t => t.Name).Should().BeEquivalentTo(
            ApplicationInsightsToolProvider.GetEnvironmentsToolName,
            ApplicationInsightsToolProvider.SearchLogsToolName,
            ApplicationInsightsToolProvider.GetLogDetailToolName,
            ApplicationInsightsToolProvider.ListSummariesToolName,
            ApplicationInsightsToolProvider.GetSummaryToolName,
            ApplicationInsightsToolProvider.LookupIncidentToolName,
            ApplicationInsightsToolProvider.LookupCorrelationToolName);
    }

    [Fact]
    public async Task Calling_DataRead_tool_at_Metadata_level_returns_error()
    {
        var sut = NewSut(new Quilt4NetMcpOptions { DataAccess = DataAccessLevel.Metadata });

        var result = await sut.CallToolAsync(
            ApplicationInsightsToolProvider.SearchLogsToolName, EmptyArgs, StubContext, default);

        result.IsError.Should().BeTrue();
        result.Content.Single().Text.Should().Contain("requires DataAccessLevel.DataRead");
    }

    [Fact]
    public async Task Get_environments_calls_service_and_returns_payload()
    {
        var ais = new Mock<IApplicationInsightsService>();
        ais.Setup(x => x.GetEnvironments(It.IsAny<IApplicationInsightsContext>()))
           .Returns(ToAsync(new EnvironmentOption("Production"), new EnvironmentOption("Test")));

        var sut = new ApplicationInsightsToolProvider(ais.Object, new Quilt4NetMcpOptions());

        var result = await sut.CallToolAsync(
            ApplicationInsightsToolProvider.GetEnvironmentsToolName, EmptyArgs, StubContext, default);

        result.IsError.Should().BeFalse();
        var payload = JsonDocument.Parse(result.Content.Single().Text).RootElement;
        payload.GetProperty("count").GetInt32().Should().Be(2);
        payload.GetProperty("environments").EnumerateArray()
            .Select(e => e.GetProperty("value").GetString())
            .Should().Contain(["Production", "Test"]);
    }

    [Fact]
    public async Task Lookup_incident_passes_id_and_lookback_to_service()
    {
        var ais = new Mock<IApplicationInsightsService>();
        ais.Setup(x => x.SearchByIncidentIdAsync(
                It.IsAny<IApplicationInsightsContext>(), "ABC123", TimeSpan.FromHours(2)))
           .Returns(ToAsync(new LogItem
           {
               Id = "row1",
               Source = LogSource.Exception,
               Message = "boom",
               Fingerprint = "fp",
               TimeGenerated = DateTime.UtcNow,
               Environment = "Production",
               Application = "Quilt4Net.Server",
               SeverityLevel = SeverityLevel.Error
           }))
           .Verifiable();

        var sut = new ApplicationInsightsToolProvider(ais.Object,
            new Quilt4NetMcpOptions { DataAccess = DataAccessLevel.DataRead });

        var args = JsonDocument.Parse("""{"incidentId":"ABC123","lookbackHours":2}""").RootElement;
        var result = await sut.CallToolAsync(
            ApplicationInsightsToolProvider.LookupIncidentToolName, args, StubContext, default);

        result.IsError.Should().BeFalse();
        ais.Verify();
        var payload = JsonDocument.Parse(result.Content.Single().Text).RootElement;
        payload.GetProperty("count").GetInt32().Should().Be(1);
        payload.GetProperty("incidentId").GetString().Should().Be("ABC123");
        payload.GetProperty("lookbackHours").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Lookback_is_clamped_to_MaxLookback()
    {
        var ais = new Mock<IApplicationInsightsService>();
        ais.Setup(x => x.SearchByIncidentIdAsync(
                It.IsAny<IApplicationInsightsContext>(), It.IsAny<string>(), TimeSpan.FromHours(24)))
           .Returns(ToAsync<LogItem>())
           .Verifiable();

        var sut = new ApplicationInsightsToolProvider(ais.Object,
            new Quilt4NetMcpOptions
            {
                DataAccess = DataAccessLevel.DataRead,
                MaxLookback = TimeSpan.FromHours(24)
            });

        // Request 999h — should clamp to MaxLookback (24h).
        var args = JsonDocument.Parse("""{"incidentId":"X","lookbackHours":999}""").RootElement;
        var result = await sut.CallToolAsync(
            ApplicationInsightsToolProvider.LookupIncidentToolName, args, StubContext, default);

        result.IsError.Should().BeFalse();
        ais.Verify();
        JsonDocument.Parse(result.Content.Single().Text).RootElement
            .GetProperty("lookbackHours").GetInt32().Should().Be(24);
    }

    [Fact]
    public async Task Default_lookback_is_used_when_arg_missing()
    {
        var ais = new Mock<IApplicationInsightsService>();
        ais.Setup(x => x.SearchByCorrelationIdAsync(
                It.IsAny<IApplicationInsightsContext>(), It.IsAny<string>(), TimeSpan.FromHours(6)))
           .Returns(ToAsync<LogItem>())
           .Verifiable();

        var sut = new ApplicationInsightsToolProvider(ais.Object,
            new Quilt4NetMcpOptions
            {
                DataAccess = DataAccessLevel.DataRead,
                DefaultLookback = TimeSpan.FromHours(6)
            });

        var args = JsonDocument.Parse("""{"correlationId":"abc"}""").RootElement;
        await sut.CallToolAsync(
            ApplicationInsightsToolProvider.LookupCorrelationToolName, args, StubContext, default);

        ais.Verify();
    }

    [Fact]
    public async Task Unknown_tool_returns_error()
    {
        var sut = NewSut(new Quilt4NetMcpOptions());

        var result = await sut.CallToolAsync("quilt4net.bogus", EmptyArgs, StubContext, default);

        result.IsError.Should().BeTrue();
        result.Content.Single().Text.Should().Contain("Unknown tool");
    }

    private static ApplicationInsightsToolProvider NewSut(Quilt4NetMcpOptions options)
        => new(Mock.Of<IApplicationInsightsService>(), options);

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
