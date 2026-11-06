using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quilt4Net.Toolkit.Blazor.Features.Configuration;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Quilt4Net.Toolkit.Framework;
using Radzen;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

/// <summary>
/// <c>RemoteConfigurationAdmin</c> rendered only Key, Value, Default, Ttl and LastUsed, while
/// <c>GetAllAsync</c> deliberately returns every entry the team key can read. One key therefore
/// appeared once per application × environment × instance as apparent duplicate rows, with nothing
/// on screen saying which scope each was — and the row's own edit/reset/delete passed
/// <c>context.Application/Environment/Instance</c>, so the operator acted on a scope the UI never
/// showed them.
/// </summary>
public class RemoteConfigurationAdminScopeTests : BunitContext
{
    private readonly StubConfigurationService _configuration = new();

    public RemoteConfigurationAdminScopeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IRemoteConfigurationService>(_configuration);
        Services.AddSingleton<IConnectionService>(new ConnectedService());
        // RadzenDataGrid and the ActionButton in the delete column property-inject Radzen's
        // DialogService / NotificationService / TooltipService — register the family rather than
        // chase them one at a time.
        Services.AddRadzenComponents();
    }

    [Fact]
    public void The_grid_names_the_scope_of_every_row()
    {
        _configuration.Entries =
        [
            Entry("Feature.A", application: "Quilt4Net.Server", environment: "Production"),
        ];

        var cut = RenderAdmin("Production");

        var headers = cut.FindAll("th").Select(x => x.TextContent).ToArray();
        headers.Should().Contain(x => x.Contains("Application"));
        headers.Should().Contain(x => x.Contains("Environment"));

        cut.Markup.Should().Contain("Quilt4Net.Server");
    }

    [Fact]
    public void A_shared_entry_is_labelled_rather_than_left_blank()
    {
        _configuration.Entries = [Entry("Feature.Shared", application: null, environment: null)];

        var cut = RenderAdmin("Production");

        cut.Markup.Should().Contain("(all)");
    }

    [Fact]
    public void The_environment_filter_defaults_to_the_host_environment()
    {
        _configuration.Entries =
        [
            Entry("Feature.A", environment: "Production"),
            Entry("Feature.B", environment: "Development"),
        ];

        var cut = RenderAdmin("Production");

        cut.Markup.Should().Contain("Feature.A");
        cut.Markup.Should().NotContain("Feature.B");
    }

    [Fact]
    public void The_filter_never_hides_a_shared_entry()
    {
        // The failure this guards: a shared entry has no environment, so a naive equality filter
        // drops it and the operator loses configuration that applies to the environment they picked.
        _configuration.Entries =
        [
            Entry("Feature.Scoped", environment: "Production"),
            Entry("Feature.Shared", environment: null),
        ];

        var cut = RenderAdmin("Production");

        cut.Markup.Should().Contain("Feature.Shared");
    }

    [Fact]
    public void An_unrepresented_host_environment_shows_everything_instead_of_an_arbitrary_slice()
    {
        _configuration.Entries =
        [
            Entry("Feature.A", environment: "Production"),
            Entry("Feature.B", environment: "Development"),
        ];

        var cut = RenderAdmin("Staging");

        cut.Markup.Should().Contain("Feature.A");
        cut.Markup.Should().Contain("Feature.B");
    }

    [Fact]
    public void There_is_no_environment_filter_when_every_entry_is_shared()
    {
        _configuration.Entries = [Entry("Feature.Shared", environment: null)];

        var cut = RenderAdmin("Production");

        cut.FindAll(".rz-dropdown").Should().BeEmpty();
    }

    [Fact]
    public void The_instance_column_is_absent_until_an_entry_carries_an_instance()
    {
        _configuration.Entries = [Entry("Feature.A", environment: "Production")];

        var cut = RenderAdmin("Production");

        cut.FindAll("th").Select(x => x.TextContent).Should().NotContain(x => x.Contains("Instance"));
    }

    [Fact]
    public void The_instance_column_appears_when_an_entry_carries_an_instance()
    {
        _configuration.Entries =
        [
            Entry("Feature.A", environment: "Production", instance: "worker-1"),
        ];

        var cut = RenderAdmin("Production");

        cut.FindAll("th").Select(x => x.TextContent).Should().Contain(x => x.Contains("Instance"));
        cut.Markup.Should().Contain("worker-1");
    }

    [Fact]
    public void The_kind_filter_counts_feature_toggles_and_other_values()
    {
        _configuration.Entries =
        [
            Entry("Feature.A", environment: "Production"),
            Entry("Feature.B", environment: "Production"),
            Entry("MaxRetries", environment: "Production", valueType: "Int32"),
        ];

        var cut = RenderAdmin("Production");

        KindOptions(cut).Should().Equal("All (3)", "Feature toggles (2)", "Other values (1)");
    }

    [Fact]
    public void The_kind_counts_follow_the_selected_environment()
    {
        _configuration.Entries =
        [
            Entry("Feature.A", environment: "Production"),
            Entry("MaxRetries", environment: "Development", valueType: "Int32"),
        ];

        var cut = RenderAdmin("Production");

        KindOptions(cut).Should().Equal("All (1)", "Feature toggles (1)", "Other values (0)");
    }

    [Fact]
    public void Choosing_feature_toggles_lists_only_true_false_entries()
    {
        _configuration.Entries =
        [
            Entry("Feature.A", environment: "Production"),
            Entry("MaxRetries", environment: "Production", valueType: "Int32"),
        ];

        var cut = RenderAdmin("Production");
        cut.Markup.Should().Contain("MaxRetries");

        cut.FindAll(".rz-selectbar .rz-button").First(x => x.TextContent.Contains("Feature toggles")).Click();

        cut.Markup.Should().Contain("Feature.A");
        cut.Markup.Should().NotContain("MaxRetries");
    }

    [Fact]
    public void Toggles_only_lists_feature_toggles_without_the_kind_filter()
    {
        _configuration.Entries =
        [
            Entry("Feature.A", environment: "Production"),
            Entry("MaxRetries", environment: "Production", valueType: "Int32"),
        ];

        Services.AddSingleton<IHostEnvironment>(new StubHostEnvironment { EnvironmentName = "Production" });
        var cut = Render<RemoteConfigurationAdmin>(p => p.Add(x => x.TogglesOnly, true));

        cut.Markup.Should().Contain("Feature.A");
        cut.Markup.Should().NotContain("MaxRetries");
        cut.FindAll(".rz-selectbar").Should().BeEmpty();
    }

    private static string[] KindOptions(IRenderedComponent<RemoteConfigurationAdmin> cut) =>
        cut.FindAll(".rz-selectbar .rz-button").Select(x => x.TextContent.Trim()).ToArray();

    private IRenderedComponent<RemoteConfigurationAdmin> RenderAdmin(string hostEnvironment)
    {
        Services.AddSingleton<IHostEnvironment>(new StubHostEnvironment { EnvironmentName = hostEnvironment });

        return Render<RemoteConfigurationAdmin>();
    }

    private static ConfigurationResponse Entry(string key, string application = null, string environment = null, string instance = null, string valueType = "Boolean")
    {
        return new ConfigurationResponse
        {
            Key = key,
            Application = application,
            Environment = environment,
            Instance = instance,
            Value = "true",
            DefaultValue = "true",
            ValueType = valueType,
            LastUsed = null,
            Ttl = null,
        };
    }

    private sealed class StubConfigurationService : IRemoteConfigurationService
    {
        public ConfigurationResponse[] Entries { get; set; } = [];

        public Task<ConfigurationResponse[]> GetAsync() => Task.FromResult(Entries);

        public ValueTask<T> GetAsync<T>(string key, T fallback = default, TimeSpan? ttl = null, string application = "") => new(fallback);
        public ValueTask<bool> GetToggleAsync(string key, bool fallback = default, TimeSpan? ttl = null, string application = "") => new(fallback);
        public Task DeleteAsync(string key, string application, string environment, string instance) => Task.CompletedTask;
        public Task SetAsync(string key, string application, string environment, string instance, string value) => Task.CompletedTask;
    }

    /// <summary>A probe that has already succeeded, so <c>ConnectionWrapper</c> renders its child.</summary>
    private sealed class ConnectedService : IConnectionService
    {
        public Task<ConnectionResult> CanConnectAsync(Service service)
        {
            return Task.FromResult(new ConnectionResult
            {
                Success = true,
                Address = new Uri("https://quilt4net.com"),
                Capabilities = new WhoAmIResponse { Scopes = ["config:write"] },
            });
        }
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
    }
}
