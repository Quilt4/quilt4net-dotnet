using Bunit;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit;
using Quilt4Net.Toolkit.Blazor.Features.VersionMatrix;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

/// <summary>
/// The version matrix fans out across every configured workspace and merges them into one grid.
/// A single broken configuration (e.g. a stale ClientSecret on one workspace) must not blank the
/// whole page — the working workspaces still render and the broken one is downgraded to a non-fatal
/// "skipped" warning. Only when <i>every</i> configuration fails does the page show a fatal error.
/// Regression for the prod incident where one bad AI config blanked /monitor/version while
/// /monitor/log (single-config) kept working.
/// </summary>
public class VersionMatrixDisplayResilienceTests : BunitContext
{
    private const string GoodWorkspace = "good-workspace-id";
    private const string BadWorkspace = "bad-workspace-id";

    public VersionMatrixDisplayResilienceTests()
    {
        Services.AddLogging();
        Services.AddSingleton(Options.Create(new ApplicationInsightsOptions()));
    }

    private static ApplicationInsightsConfigurationResponse Config(string name, string workspaceId) => new()
    {
        Id = workspaceId,
        Name = name,
        TenantId = "tenant",
        WorkspaceId = workspaceId,
        ClientId = "client",
        ClientSecret = "secret",
    };

    [Fact]
    public void One_failing_config_still_renders_the_others_and_warns()
    {
        Services.AddSingleton<IVersionMatrixService>(new PartialFailureVersionMatrixService());

        var cut = Render<VersionMatrixDisplay>(p => p
            .Add(c => c.Configs, new[] { Config("Good config", GoodWorkspace), Config("Bad config", BadWorkspace) })
            .Add(c => c.ConfigurationPath, "/monitor/configuration"));

        cut.WaitForAssertion(() =>
        {
            // The healthy workspace's data renders.
            cut.Markup.Should().Contain("WebApp");
            cut.Markup.Should().Contain("1.2.3");
            // The broken one is a non-fatal warning that names the config and offers the fix link.
            cut.Markup.Should().Contain("skipped");
            cut.Markup.Should().Contain("Bad config");
            cut.Markup.Should().Contain("Edit configuration");
            // ...and NOT the fatal "whole page failed" alert.
            cut.Markup.Should().NotContain("Failed to load version matrix");
        });
    }

    [Fact]
    public void All_failing_configs_show_the_fatal_error_with_an_incident_id()
    {
        Services.AddSingleton<IVersionMatrixService>(new AllFailVersionMatrixService());

        var cut = Render<VersionMatrixDisplay>(p => p
            .Add(c => c.Configs, new[] { Config("First", BadWorkspace), Config("Second", BadWorkspace + "-2") })
            .Add(c => c.ConfigurationPath, "/monitor/configuration"));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Failed to load version matrix");
            cut.Markup.Should().MatchRegex(@"\[Incident:\s+[2-9A-HJ-NP-Z]{6}\]");
            cut.Markup.Should().Contain("Edit configuration");
            // No grid rendered.
            cut.Markup.Should().NotContain("WebApp");
        });
    }

    private sealed class PartialFailureVersionMatrixService : IVersionMatrixService
    {
        public Task<VersionMatrixView> GetAsync(IApplicationInsightsContext context, TimeSpan? lookback = null, CancellationToken cancellationToken = default)
        {
            if (context.WorkspaceId == BadWorkspace)
                // "AADSTS" in the message makes ApplicationInsightsErrorMessage classify it as an auth failure.
                throw new InvalidOperationException("AADSTS7000215: Invalid client secret provided.");

            return Task.FromResult(VersionMatrixView.FromCells(new[]
            {
                new VersionMatrixCell
                {
                    ApplicationName = "WebApp",
                    Environment = "Production",
                    Version = "1.2.3",
                    LastSeen = DateTime.UtcNow,
                    Source = VersionMatrixSource.Startup,
                },
            }));
        }

        public Task<VersionMatrixView> RefreshAsync(IApplicationInsightsContext context, TimeSpan? lookback = null, CancellationToken cancellationToken = default)
            => GetAsync(context, lookback, cancellationToken);
    }

    private sealed class AllFailVersionMatrixService : IVersionMatrixService
    {
        public Task<VersionMatrixView> GetAsync(IApplicationInsightsContext context, TimeSpan? lookback = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("AADSTS7000215: Invalid client secret provided.");

        public Task<VersionMatrixView> RefreshAsync(IApplicationInsightsContext context, TimeSpan? lookback = null, CancellationToken cancellationToken = default)
            => GetAsync(context, lookback, cancellationToken);
    }
}
