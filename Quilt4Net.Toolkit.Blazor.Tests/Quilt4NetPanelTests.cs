using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Quilt4Net.Toolkit.Blazor.Features.Panel;
using Quilt4Net.Toolkit.Blazor.Framework;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Quilt4Net.Toolkit.Features.Health;
using Quilt4Net.Toolkit.Features.HeartbeatReporting;
using Quilt4Net.Toolkit.Features.Notification;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

/// <summary>
/// The Quilt4Net panel (#144): a tab per service the host registered, hidden when the team has its area
/// off or the key lacks its scope, and each tab a component of its own.
/// </summary>
public class Quilt4NetPanelTests : BunitContext
{
    public Quilt4NetPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Only_the_connection_tab_shows_when_the_host_registered_nothing()
    {
        Quilt4NetPanelVisibility.Visible(Services, [], Quilt4NetPanelTabs.All, inBrowser: false)
            .Should().Be(Quilt4NetPanelTabs.Connection);
    }

    [Fact]
    public void Every_registered_service_has_its_tab_while_the_server_does_not_say_which_areas_are_on()
    {
        RegisterAll();

        Quilt4NetPanelVisibility.Visible(Services, [], Quilt4NetPanelTabs.All, inBrowser: false)
            .Should().Be(Quilt4NetPanelTabs.All);
    }

    [Fact]
    public void A_tab_whose_area_the_team_switched_off_is_hidden()
    {
        RegisterAll();
        var checks = new[] { Check("Content", new WhoAmIResponse { Areas = ["Content", "Configuration"], Scopes = ["notification:write"] }) };

        var visible = Quilt4NetPanelVisibility.Visible(Services, checks, Quilt4NetPanelTabs.All, inBrowser: false);

        visible.Should().HaveFlag(Quilt4NetPanelTabs.Content).And.HaveFlag(Quilt4NetPanelTabs.Health);
        visible.Should().NotHaveFlag(Quilt4NetPanelTabs.Heartbeat).And.NotHaveFlag(Quilt4NetPanelTabs.Notifications, "Monitoring is off");
    }

    [Fact]
    public void Sending_is_hidden_without_the_scope_and_in_the_browser()
    {
        RegisterAll();
        var noScope = new[] { Check("Notifications", new WhoAmIResponse { Scopes = ["notification:read"] }) };

        Quilt4NetPanelVisibility.Visible(Services, noScope, Quilt4NetPanelTabs.All, inBrowser: false).Should().NotHaveFlag(Quilt4NetPanelTabs.Notifications);
        Quilt4NetPanelVisibility.Visible(Services, [], Quilt4NetPanelTabs.All, inBrowser: true).Should().NotHaveFlag(Quilt4NetPanelTabs.Notifications, "the key would be public in the browser");
        Quilt4NetPanelVisibility.Visible(Services, [], Quilt4NetPanelTabs.Connection | Quilt4NetPanelTabs.Health, inBrowser: false)
            .Should().Be(Quilt4NetPanelTabs.Connection | Quilt4NetPanelTabs.Health, "the host chooses which tabs the panel may show");
    }

    [Fact]
    public void The_heartbeat_tab_shows_what_the_instance_reports_and_the_server_s_answer()
    {
        var status = new Mock<IHeartbeatStatus>();
        status.Setup(x => x.Current).Returns(new HeartbeatState
        {
            Enabled = true, HasApiKey = true, Interval = TimeSpan.FromMinutes(5), LastAttemptUtc = DateTime.UtcNow.AddMinutes(-1), LastStatusCode = 403,
            LastProblem = "Monitoring is switched off for the team.",
            LastReport = new HeartbeatReport("Worker", "Production", "node-1", "2.4.1", ["import"]) { Metrics = [new HeartbeatMetric("cpu.process", 41.5, "percent")] }
        });
        Services.AddSingleton(status.Object);
        // The value is written in the reader's culture; this machine's may write 41,5.
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        var cut = Render<Quilt4NetHeartbeatView>();

        cut.Markup.Should().Contain("Refused (403)").And.Contain("Monitoring is switched off for the team.")
            .And.Contain("import").And.Contain("cpu.process").And.Contain("41.5 %");
    }

    [Fact]
    public void Sending_says_when_no_phone_will_hear_it()
    {
        var client = new Mock<INotificationClient>();
        client.Setup(x => x.GetCategoriesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(["alarm", "info"]);
        client.Setup(x => x.SendAsync("info", "Hello", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationResponse("1", "info", "Hello", "App", "Test", "pc", DateTime.UtcNow) { Reached = 0 });
        Services.AddSingleton(client.Object);

        var cut = Render<Quilt4NetNotificationSender>();
        cut.Find("input[maxlength='4000']").Input("Hello");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Send")).Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("nobody will hear it"));
        client.Verify(x => x.SendAsync("info", "Hello", It.IsAny<CancellationToken>()));
    }

    [Fact]
    public void Finding_a_text_searches_the_cached_keys_and_texts_and_says_where_each_came_from()
    {
        var content = new Mock<IRemoteContentCallService>();
        content.Setup(x => x.GetCacheStatus()).Returns([new ContentCacheLanguageStatus(Guid.Empty, 2, 1, DateTime.UtcNow.AddMinutes(9), null)]);
        content.Setup(x => x.GetCachedEntries(It.IsAny<Guid?>())).Returns(
        [
            new ContentCacheEntry("menu.home", "App", Guid.Empty, "Home", DateTime.UtcNow.AddMinutes(9), false, null),
            new ContentCacheEntry("footer.text", "App", Guid.Empty, "Made with the menu", DateTime.UtcNow.AddMinutes(9), true, null)
        ]);
        var language = new Mock<ILanguageStateService>();
        language.Setup(x => x.Languages).Returns([]);
        Services.AddSingleton(content.Object);
        Services.AddSingleton(language.Object);
        Services.AddSingleton(Mock.Of<IContentService>());
        Services.AddSingleton(Mock.Of<IEditContentService>());
        Services.AddSingleton(Mock.Of<IContentSourceService>());
        Services.AddSingleton(Mock.Of<IContentAdminService>());
        Services.AddSingleton<IConnectionService>(new Connected());

        var cut = Render<Quilt4Net.Toolkit.Blazor.Features.Language.ContentAdmin>();
        cut.Find("input[placeholder='Key or text']").Input("menu");

        cut.Markup.Should().Contain("menu.home").And.Contain("footer.text", "the text matches too")
            .And.Contain("Server").And.Contain("Default in code");
    }

    private void RegisterAll()
    {
        Services.AddSingleton(Mock.Of<IRemoteContentCallService>());
        Services.AddSingleton(Mock.Of<ILanguageStateService>());
        Services.AddSingleton(Mock.Of<IRemoteConfigurationService>());
        Services.AddSingleton(Mock.Of<IHeartbeatStatus>());
        Services.AddSingleton(Mock.Of<INotificationClient>());
        Services.AddSingleton(Mock.Of<IHealthService>());
    }

    private static Quilt4NetEndpointCheck Check(string feature, WhoAmIResponse whoAmI)
        => new(new Quilt4NetEndpoint(feature, "https://quilt4net.com/", "key"), true, 200, 12, whoAmI, "4.3.66", null);

    private sealed class Connected : IConnectionService
    {
        public Task<ConnectionResult> CanConnectAsync(Service service) => Task.FromResult(new ConnectionResult { Success = true });
    }
}
