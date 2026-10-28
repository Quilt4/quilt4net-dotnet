using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Features.HeartbeatReporting;
using Quilt4Net.Toolkit.Framework;

namespace Quilt4Net.Toolkit;

/// <summary>
/// Registration for reporting heartbeats with capabilities to Quilt4Net.
/// </summary>
public static class HeartbeatReportingRegistration
{
    /// <summary>
    /// Starts reporting this instance's capabilities to Quilt4Net on an interval.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="options">Optional overrides applied after configuration is bound.</param>
    public static void AddQuilt4NetHeartbeatReporting(this IHostApplicationBuilder builder, Action<HeartbeatReportingOptions> options = null)
    {
        builder.Services.AddQuilt4NetHeartbeatReporting(builder.Configuration, options);
    }

    /// <summary>
    /// Starts reporting this instance's capabilities to Quilt4Net on an interval.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration to bind <c>Quilt4Net:HeartbeatReporting</c> from.</param>
    /// <param name="options">Optional overrides applied after configuration is bound.</param>
    /// <exception cref="InvalidOperationException">The configured address is not an absolute URI, or the interval is not positive.</exception>
    public static void AddQuilt4NetHeartbeatReporting(this IServiceCollection services, IConfiguration configuration, Action<HeartbeatReportingOptions> options = null)
    {
        var apiKey = configuration?.GetSection("Quilt4Net").GetSection("ApiKey").Value;
        var address = configuration?.GetSection("Quilt4Net").GetSection("Quilt4NetAddress").Value;

        var config = configuration?.GetSection("Quilt4Net:HeartbeatReporting").Get<HeartbeatReportingOptions>();
        var o = config ?? new HeartbeatReportingOptions();
        o.ApiKey = config?.ApiKey ?? apiKey;
        o.Quilt4NetAddress = config?.Quilt4NetAddress ?? address ?? "https://quilt4net.com/";

        options?.Invoke(o);

        // Validated after the callback, for the same reason as AddQuilt4NetIssues.
        if (!Uri.TryCreate(o.Quilt4NetAddress, UriKind.Absolute, out _)) throw new InvalidOperationException($"Configuration {nameof(o.Quilt4NetAddress)} with value '{o.Quilt4NetAddress}' cannot be parsed to an absolute uri.");
        if (o.Interval <= TimeSpan.Zero) throw new InvalidOperationException($"Configuration {nameof(o.Interval)} must be positive.");
        if (o.MetricTimeout <= TimeSpan.Zero) throw new InvalidOperationException($"Configuration {nameof(o.MetricTimeout)} must be positive.");

        services.AddSingleton(Options.Create(o));

        // Switched off: the options stay readable, but nothing reports and no HTTP client is set up.
        if (!o.Enabled) return;

        services.AddQuilt4NetCorrelationId();
        services.AddHttpClient(HeartbeatReporter.HttpClientName, client =>
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

        // Scoped, and resolved in a scope per heartbeat, so a provider can take a scoped service such as
        // a database context. A provider registered directly as IHeartbeatMetricProvider is used too.
        foreach (var provider in o.MetricProviders)
        {
            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IHeartbeatMetricProvider), provider));
        }

        services.AddHostedService<HeartbeatReporter>();
    }
}
