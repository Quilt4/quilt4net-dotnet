using Bunit;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Blazor.Features.Log;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Radzen;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class LogComponentsServiceMissingTests : BunitContext
{
    public LogComponentsServiceMissingTests()
    {
        // Required by some Log components but unrelated to the AI client guard.
        Services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        Services.AddScoped<DialogService>();
    }

    [Fact]
    public void LogEnvironmentSelector_Renders_Info_When_ApplicationInsightsService_Not_Registered()
    {
        var cut = Render<LogEnvironmentSelector>();

        cut.Markup.Should().Contain(LogConfigurationGuard.ServiceNotRegisteredMessage);
    }

    [Fact]
    public void LogSearchView_Renders_Info_When_ApplicationInsightsService_Not_Registered()
    {
        var cut = Render<LogSearchView>();

        cut.Markup.Should().Contain(LogConfigurationGuard.ServiceNotRegisteredMessage);
    }

    [Fact]
    public void LogSummaryListView_Renders_Info_When_ApplicationInsightsService_Not_Registered()
    {
        var cut = Render<LogSummaryListView>();

        cut.Markup.Should().Contain(LogConfigurationGuard.ServiceNotRegisteredMessage);
    }

    [Fact]
    public void LogMeasureView_Renders_Info_When_ApplicationInsightsService_Not_Registered()
    {
        var cut = Render<LogMeasureView>();

        cut.Markup.Should().Contain(LogConfigurationGuard.ServiceNotRegisteredMessage);
    }

    [Fact]
    public void LogCountView_Renders_Info_When_ApplicationInsightsService_Not_Registered()
    {
        var cut = Render<LogCountView>();

        cut.Markup.Should().Contain(LogConfigurationGuard.ServiceNotRegisteredMessage);
    }

    [Fact]
    public void LogDetailContent_Renders_Info_When_ApplicationInsightsService_Not_Registered()
    {
        var cut = Render<LogDetailContent>();

        cut.Markup.Should().Contain(LogConfigurationGuard.ServiceNotRegisteredMessage);
    }

    [Fact]
    public void LogSummaryContent_Renders_Info_When_ApplicationInsightsService_Not_Registered()
    {
        var cut = Render<LogSummaryContent>();

        cut.Markup.Should().Contain(LogConfigurationGuard.ServiceNotRegisteredMessage);
    }

    [Fact]
    public void LogView_Renders_Info_When_ApplicationInsightsService_Not_Registered()
    {
        Services.AddSingleton(Options.Create(new ApplicationInsightsOptions
        {
            TenantId = "tenant",
            WorkspaceId = "workspace",
            ClientId = "client",
            ClientSecret = "secret"
        }));

        var cut = Render<LogView>();

        cut.Markup.Should().Contain(LogConfigurationGuard.ServiceNotRegisteredMessage);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
