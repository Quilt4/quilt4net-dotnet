using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Api.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Api.Tests;

/// <summary>
/// Application Insights SDK 3.x (OpenTelemetry based) has no RequestTelemetry on the HttpContext,
/// so request data must land as tags on the ASP.NET Core request span to reach AppRequests.
/// </summary>
public class RequestActivityLoggingTests
{
    private static async Task<(HttpResponseMessage Response, Activity Activity)> SendAsync(HttpRequestLogMode mode, string path, Func<HttpContext, Task> handler)
    {
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var options = new LoggingOptions { LogHttpRequest = mode };
        builder.Services.AddSingleton(Options.Create(options));
        builder.Services.AddSingleton(new CompiledLoggingOptions(options));

        await using var app = builder.Build();
        app.UseMiddleware<RequestResponseLoggingMiddleware>();
        ((IApplicationBuilder)app).Run(new RequestDelegate(handler));
        await app.StartAsync();

        HttpResponseMessage response = null;
        try
        {
            response = await app.GetTestClient().GetAsync(path);
        }
        catch (InvalidOperationException)
        {
            //NOTE: TestServer rethrows an unhandled handler exception to the client.
        }
        //NOTE: When the handler throws, TestServer hands the exception to the client before hosting has
        // disposed the request context, which is where the span is stopped. Wait for it rather than race it.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        Activity activity;
        while ((activity = stopped.SingleOrDefault(a => a.OperationName == "Microsoft.AspNetCore.Hosting.HttpRequestIn")) == null
               && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        await app.StopAsync();

        return (response, activity);
    }

    [Fact]
    public async Task ApplicationInsights_mode_tags_the_request_span()
    {
        var (response, activity) = await SendAsync(HttpRequestLogMode.ApplicationInsights, "/api/x?a=1", c => c.Response.WriteAsync("hello"));

        response.IsSuccessStatusCode.Should().BeTrue();
        activity.Should().NotBeNull();
        activity.GetTagItem("UserId").Should().Be("Anonymous");
        ((string)activity.GetTagItem("Request")).Should().Contain("/api/x");
        activity.GetTagItem("Response").Should().NotBeNull();
        activity.GetTagItem("Elapsed").Should().NotBeNull();
        activity.GetTagItem("Details").Should().NotBeNull();
        activity.Status.Should().NotBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task Exception_marks_the_request_span_as_error()
    {
        var (_, activity) = await SendAsync(HttpRequestLogMode.ApplicationInsights, "/api/x", _ => throw new InvalidOperationException("boom"));

        activity.Should().NotBeNull();
        activity.GetTagItem("ExceptionMessage").Should().Be("boom");
        activity.Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task Logger_mode_leaves_the_request_span_alone()
    {
        var (_, activity) = await SendAsync(HttpRequestLogMode.Logger, "/api/x", c => c.Response.WriteAsync("hello"));

        activity.Should().NotBeNull();
        activity.GetTagItem("Request").Should().BeNull();
    }

    [Fact]
    public async Task Paths_outside_IncludePaths_are_not_tagged()
    {
        var (_, activity) = await SendAsync(HttpRequestLogMode.ApplicationInsights, "/other", c => c.Response.WriteAsync("hello"));

        activity.Should().NotBeNull();
        activity.GetTagItem("Request").Should().BeNull();
    }
}
