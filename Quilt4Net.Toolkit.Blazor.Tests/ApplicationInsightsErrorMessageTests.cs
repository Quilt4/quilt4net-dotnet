using AwesomeAssertions;
using Quilt4Net.Toolkit.Blazor.Features.Log;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class ApplicationInsightsErrorMessageTests
{
    [Fact]
    public void Format_without_incidentId_omits_incident_suffix()
    {
        var msg = ApplicationInsightsErrorMessage.Format("Could not load.", new InvalidOperationException("boom"));

        msg.Should().Be("Could not load. boom");
        msg.Should().NotContain("[Incident:");
    }

    [Fact]
    public void Format_with_incidentId_appends_bracketed_incident_suffix()
    {
        var msg = ApplicationInsightsErrorMessage.Format("Could not load.", new InvalidOperationException("boom"), "K7XQ4P");

        msg.Should().Be("Could not load. boom [Incident: K7XQ4P]");
    }

    [Fact]
    public void Format_authentication_failure_includes_friendly_text_and_incident()
    {
        var inner = new InvalidOperationException("AADSTS7000215: Invalid client secret provided.");
        var outer = new Exception("wrapper", inner);

        var msg = ApplicationInsightsErrorMessage.Format("Could not load.", outer, "K7XQ4P");

        msg.Should().StartWith("Could not load. Application Insights authentication failed");
        msg.Should().EndWith("[Incident: K7XQ4P]");
    }

    [Fact]
    public void Format_with_null_incidentId_behaves_like_two_arg_overload()
    {
        var msg = ApplicationInsightsErrorMessage.Format("Could not load.", new InvalidOperationException("boom"), null);
        msg.Should().NotContain("[Incident:");
    }

    [Fact]
    public void Format_with_empty_incidentId_behaves_like_two_arg_overload()
    {
        var msg = ApplicationInsightsErrorMessage.Format("Could not load.", new InvalidOperationException("boom"), string.Empty);
        msg.Should().NotContain("[Incident:");
    }

    [Fact]
    public void Format_authentication_failure_with_context_identifies_failing_workspace()
    {
        var inner = new InvalidOperationException("AADSTS7000215: Invalid client secret provided.");
        var outer = new Exception("wrapper", inner);
        var ctx = new TestContext { WorkspaceId = "03abd6ba-a499-44e8-94bc-96d2500a3161" };

        var msg = ApplicationInsightsErrorMessage.Format("Could not load.", outer, "K7XQ4P", ctx);

        msg.Should().Contain("for workspace 03abd6ba-a499-44e8-94bc-96d2500a3161");
        msg.Should().EndWith("[Incident: K7XQ4P]");
    }

    [Fact]
    public void Format_authentication_failure_without_context_omits_workspace_clause()
    {
        var inner = new InvalidOperationException("AADSTS7000215: Invalid client secret provided.");
        var outer = new Exception("wrapper", inner);

        var msg = ApplicationInsightsErrorMessage.Format("Could not load.", outer, "K7XQ4P", null);

        msg.Should().NotContain("for workspace");
        msg.Should().StartWith("Could not load. Application Insights authentication failed");
    }

    [Fact]
    public void Format_non_auth_failure_with_context_uses_exception_message_unchanged()
    {
        var ctx = new TestContext { WorkspaceId = "ws-id" };

        var msg = ApplicationInsightsErrorMessage.Format("Could not load.", new InvalidOperationException("boom"), "K7XQ4P", ctx);

        msg.Should().Be("Could not load. boom [Incident: K7XQ4P]");
    }

    private sealed class TestContext : IApplicationInsightsContext
    {
        public string TenantId { get; init; }
        public string WorkspaceId { get; init; }
        public string ClientId { get; init; }
        public string ClientSecret { get; init; }
    }
}
