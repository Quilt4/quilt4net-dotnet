using System;
using Bunit;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Quilt4Net.Toolkit.Blazor.Features.Log;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

/// <summary>
/// #126: expanding a summary row must scope the instance list to the row's application. This pins
/// the wiring — <see cref="LogSummaryContent"/> forwards its <c>Application</c> parameter to
/// <c>IApplicationInsightsService.GetSummary</c> (which applies the KQL application filter).
/// </summary>
public class LogSummaryContentApplicationScopeTests : BunitContext
{
    [Fact]
    public void Drilldown_forwards_selected_application_to_GetSummary()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        string capturedApplication = "NOT-CALLED";
        var mock = new Mock<IApplicationInsightsService>();
        mock.Setup(x => x.GetSummary(
                It.IsAny<IApplicationInsightsContext>(), It.IsAny<string>(), It.IsAny<LogSource>(),
                It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<string>()))
            .Callback((IApplicationInsightsContext _, string _, LogSource _, string _, TimeSpan _, int _, string application) => capturedApplication = application)
            .ReturnsAsync(SampleSummary());

        Services.AddSingleton(mock.Object);

        Render<LogSummaryContent>(p => p
            .Add(c => c.Fingerprint, "fp-123")
            .Add(c => c.Environment, "Production")
            .Add(c => c.Application, "Eplicta.Aggregator")
            .Add(c => c.Range, (TimeSpan?)TimeSpan.FromDays(1))
            .Add(c => c.Source, (LogSource?)LogSource.Trace));

        capturedApplication.Should().Be("Eplicta.Aggregator");
    }

    private static SummaryData SampleSummary() => new()
    {
        Fingerprint = "fp-123",
        Message = "boom",
        Environment = "Production",
        Application = "Eplicta.Aggregator",
        SeverityLevel = SeverityLevel.Error,
        Source = LogSource.Trace,
        Items = [],
        TotalCount = 0
    };
}
