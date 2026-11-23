using Quilt4Net.Toolkit.Features.Api;

namespace Quilt4Net.Toolkit.Features.Health;

/// <summary>
/// Where the deep health components come from: the ones added to <see cref="Quilt4NetHealthApiOptions"/> and the
/// ones every registered <see cref="IComponentService"/> returns. Shared by the probe and the background refresher
/// so both see the same set under the same names.
/// </summary>
internal static class ComponentSource
{
    private const string DefaultName = "Component";

    public static string NameOf(Component component) => string.IsNullOrEmpty(component.Name) ? DefaultName : component.Name;

    public static IEnumerable<Component> FromServices(Quilt4NetHealthApiOptions options, IServiceProvider serviceProvider)
        => options.ComponentServices.SelectMany(x => ((IComponentService)serviceProvider.GetService(x))?.GetComponents() ?? []);

    public static IEnumerable<Component> All(Quilt4NetHealthApiOptions options, IServiceProvider serviceProvider)
        => FromServices(options, serviceProvider).Concat(options.Components);

    /// <summary>
    /// True when the component answers from its last result and is refreshed in the background:
    /// <see cref="Component.RefreshInBackground"/> together with a positive <see cref="Component.CacheDuration"/>.
    /// </summary>
    public static bool IsRefreshedInBackground(Component component)
        => component.RefreshInBackground && component.CacheDuration is { } duration && duration > TimeSpan.Zero;
}
