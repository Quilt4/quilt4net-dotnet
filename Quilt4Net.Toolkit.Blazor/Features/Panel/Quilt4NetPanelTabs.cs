using Microsoft.Extensions.DependencyInjection;
using Quilt4Net.Toolkit.Blazor.Framework;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Quilt4Net.Toolkit.Features.Health;
using Quilt4Net.Toolkit.Features.HeartbeatReporting;
using Quilt4Net.Toolkit.Features.Notification;

namespace Quilt4Net.Toolkit.Blazor.Features.Panel;

/// <summary>The tabs of <c>Quilt4NetPanel</c> (#144). Combine them to choose which the panel may show.</summary>
[Flags]
public enum Quilt4NetPanelTabs
{
    None = 0,

    /// <summary>The application, each Quilt4Net feature's address and what it answers, the team's areas and the live link.</summary>
    Connection = 1,

    /// <summary>Content: edit mode, where text comes from, the cache per language and a search of it.</summary>
    Content = 2,

    /// <summary>Feature toggles and remote configuration.</summary>
    Configuration = 4,

    /// <summary>This instance's heartbeat: what it reports, its metrics, when it last sent and the answer.</summary>
    Heartbeat = 8,

    /// <summary>Send a notification to the team's phones.</summary>
    Notifications = 16,

    /// <summary>The application's own health checks.</summary>
    Health = 32,

    All = Connection | Content | Configuration | Heartbeat | Notifications | Health
}

/// <summary>Which tabs the panel shows: asked for, registered by the host, on for the team, allowed for the key.</summary>
internal static class Quilt4NetPanelVisibility
{
    public static Quilt4NetPanelTabs Visible(IServiceProvider services, IReadOnlyList<Quilt4NetEndpointCheck> checks, Quilt4NetPanelTabs requested, bool inBrowser)
    {
        var areas = Quilt4NetEndpoints.Areas(checks);
        bool AreaOn(string area) => areas == null || areas.Contains(area, StringComparer.OrdinalIgnoreCase);
        bool Has<T>() => services.GetService<T>() != null;
        // Hidden only when the key is known to lack the scope; an unreachable server says nothing either way.
        bool KeyAllows(string feature, string scope) => checks?.FirstOrDefault(x => x.Endpoint.Feature == feature)?.WhoAmI is not { } who || who.HasScope(scope);

        var visible = Quilt4NetPanelTabs.Connection;
        if (Has<IRemoteContentCallService>() && Has<ILanguageStateService>() && AreaOn("Content")) visible |= Quilt4NetPanelTabs.Content;
        if (Has<IRemoteConfigurationService>() && AreaOn("Configuration")) visible |= Quilt4NetPanelTabs.Configuration;
        if (Has<IHeartbeatStatus>() && AreaOn("Monitoring")) visible |= Quilt4NetPanelTabs.Heartbeat;
        if (Has<INotificationClient>() && !inBrowser && AreaOn("Monitoring") && KeyAllows("Notifications", "notification:write")) visible |= Quilt4NetPanelTabs.Notifications;
        if (Has<IHealthService>()) visible |= Quilt4NetPanelTabs.Health;
        return visible & requested;
    }
}
