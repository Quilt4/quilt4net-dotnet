using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Features.Notification;
using Quilt4Net.Toolkit.Framework;

namespace Quilt4Net.Toolkit;

/// <summary>
/// Registration for the Quilt4Net notification client.
/// </summary>
public static class NotificationRegistration
{
    /// <summary>
    /// Registers <see cref="INotificationClient"/>, which sends notifications to Quilt4Net.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="options">Optional overrides applied after configuration is bound.</param>
    public static void AddQuilt4NetNotifications(this IHostApplicationBuilder builder, Action<NotificationOptions> options = null)
    {
        builder.Services.AddQuilt4NetNotifications(builder.Configuration, options);
    }

    /// <summary>
    /// Registers <see cref="INotificationClient"/>, which sends notifications to Quilt4Net.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration to bind <c>Quilt4Net:Notification</c> from.</param>
    /// <param name="options">Optional overrides applied after configuration is bound.</param>
    /// <exception cref="InvalidOperationException">The configured address is not an absolute URI.</exception>
    public static void AddQuilt4NetNotifications(this IServiceCollection services, IConfiguration configuration, Action<NotificationOptions> options = null)
    {
        var apiKey = configuration?.GetSection("Quilt4Net").GetSection("ApiKey").Value;
        var address = configuration?.GetSection("Quilt4Net").GetSection("Quilt4NetAddress").Value;

        var config = configuration?.GetSection("Quilt4Net:Notification").Get<NotificationOptions>();
        var o = config ?? new NotificationOptions();
        o.ApiKey = config?.ApiKey ?? apiKey;
        o.Quilt4NetAddress = config?.Quilt4NetAddress ?? address ?? "https://quilt4net.com/";

        options?.Invoke(o);

        // Validated after the callback, for the same reason as AddQuilt4NetIssues: the callback is
        // where a host most often sets the address.
        if (!Uri.TryCreate(o.Quilt4NetAddress, UriKind.Absolute, out _)) throw new InvalidOperationException($"Configuration {nameof(o.Quilt4NetAddress)} with value '{o.Quilt4NetAddress}' cannot be parsed to an absolute uri.");

        services.AddSingleton(Options.Create(o));

        services.AddQuilt4NetCorrelationId();
        services.AddHttpClient(NotificationClient.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(o.Quilt4NetAddress);
                if (!string.IsNullOrEmpty(o.ApiKey))
                {
                    client.DefaultRequestHeaders.Remove("X-API-KEY");
                    client.DefaultRequestHeaders.Add("X-API-KEY", o.ApiKey);
                    client.DefaultRequestHeaders.Remove(Quilt4NetClient.HeaderName);
                    client.DefaultRequestHeaders.Add(Quilt4NetClient.HeaderName, Quilt4NetClient.Value);
                }
            })
            .AddQuilt4NetCorrelationId();

        services.AddTransient<INotificationClient, NotificationClient>();
    }
}
