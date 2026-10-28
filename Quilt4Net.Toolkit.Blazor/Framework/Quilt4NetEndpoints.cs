using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Quilt4Net.Toolkit.Features.HeartbeatReporting;
using Quilt4Net.Toolkit.Features.Issue;
using Quilt4Net.Toolkit.Features.Notification;
using Quilt4Net.Toolkit.Framework;

namespace Quilt4Net.Toolkit.Blazor.Framework;

/// <summary>A Quilt4Net feature the host registered, with the address and key it calls with (#144).</summary>
public sealed record Quilt4NetEndpoint(string Feature, string Address, string ApiKey);

/// <summary>What one address and key answered: the team and scopes behind the key, how fast, and the server's version.</summary>
/// <param name="Problem">Why it could not be reached or was refused; null when it answered.</param>
public sealed record Quilt4NetEndpointCheck(Quilt4NetEndpoint Endpoint, bool Reached, int? StatusCode, long? Milliseconds, WhoAmIResponse WhoAmI, string ServerVersion, string Problem);

/// <summary>
/// The Quilt4Net features a host has registered and what their addresses answer, for the panel's
/// Connection tab and for deciding which tabs to show (#144). Each feature has its own address and key,
/// so each is listed; an address and key used by several is called once.
/// </summary>
internal static class Quilt4NetEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Each feature the host registered, found by its service, since an options type resolves whether or not it was configured.</summary>
    public static IReadOnlyList<Quilt4NetEndpoint> Registered(IServiceProvider services)
    {
        var result = new List<Quilt4NetEndpoint>();

        void Add<TService, TOptions>(string feature, Func<TOptions, (string Address, string ApiKey)> read, Func<TOptions, bool> enabled = null) where TOptions : class
        {
            if (services.GetService<TService>() == null) return;
            var options = services.GetService<IOptions<TOptions>>()?.Value;
            if (options == null || enabled?.Invoke(options) == false) return;
            var (address, key) = read(options);
            result.Add(new Quilt4NetEndpoint(feature, address, key));
        }

        Add<IRemoteContentCallService, ContentOptions>("Content", o => (o.Quilt4NetAddress, o.ApiKey));
        Add<IRemoteConfigurationService, RemoteConfigurationOptions>("Configuration", o => (o.Quilt4NetAddress, o.ApiKey));
        Add<IIssueService, IssueOptions>("Issues", o => (o.Quilt4NetAddress, o.ApiKey));
        Add<IHeartbeatStatus, HeartbeatReportingOptions>("Heartbeat", o => (o.Quilt4NetAddress, o.ApiKey), o => o.Enabled);
        Add<INotificationClient, NotificationOptions>("Notifications", o => (o.Quilt4NetAddress, o.ApiKey));
        return result;
    }

    /// <summary>Checks every endpoint: one WhoAmI per address and key, and the server's version once per address.</summary>
    public static async Task<IReadOnlyList<Quilt4NetEndpointCheck>> CheckAsync(IServiceProvider services, IReadOnlyList<Quilt4NetEndpoint> endpoints, CancellationToken cancellationToken = default)
    {
        var factory = services.GetService<IHttpClientFactory>();
        if (factory == null) return endpoints.Select(e => new Quilt4NetEndpointCheck(e, false, null, null, null, null, "No IHttpClientFactory is registered.")).ToArray();

        var versions = new Dictionary<string, Task<string>>(StringComparer.OrdinalIgnoreCase);
        var whoAmIs = new Dictionary<(string, string), Task<(bool, int?, long?, WhoAmIResponse, string)>>();
        foreach (var endpoint in endpoints)
        {
            var address = Normalize(endpoint.Address);
            if (address == null) continue;
            if (!versions.ContainsKey(address)) versions[address] = ServerVersionAsync(factory, address, cancellationToken);
            if (!whoAmIs.ContainsKey((address, endpoint.ApiKey ?? ""))) whoAmIs[(address, endpoint.ApiKey ?? "")] = WhoAmIAsync(factory, address, endpoint.ApiKey, cancellationToken);
        }

        var result = new List<Quilt4NetEndpointCheck>();
        foreach (var endpoint in endpoints)
        {
            var address = Normalize(endpoint.Address);
            if (address == null)
            {
                result.Add(new Quilt4NetEndpointCheck(endpoint, false, null, null, null, null, $"'{endpoint.Address}' is not an address."));
                continue;
            }
            var (reached, status, ms, whoAmI, problem) = await whoAmIs[(address, endpoint.ApiKey ?? "")];
            result.Add(new Quilt4NetEndpointCheck(endpoint, reached, status, ms, whoAmI, await versions[address], problem));
        }
        return result;
    }

    /// <summary>The team's areas that are on, from the first check that says; null when none does.</summary>
    public static string[] Areas(IEnumerable<Quilt4NetEndpointCheck> checks) => checks?.Select(x => x.WhoAmI?.Areas).FirstOrDefault(x => x != null);

    private static async Task<(bool, int?, long?, WhoAmIResponse, string)> WhoAmIAsync(IHttpClientFactory factory, string address, string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(apiKey)) return (false, null, null, null, "No API key is configured.");
        try
        {
            using var client = factory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(address), "Api/System/WhoAmI"));
            request.Headers.Add("X-API-KEY", apiKey);
            var watch = Stopwatch.StartNew();
            using var response = await client.SendAsync(request, cancellationToken);
            var ms = watch.ElapsedMilliseconds;
            if (!response.IsSuccessStatusCode)
            {
                var detail = DetailOf(await response.Content.ReadAsStringAsync(cancellationToken)) ?? response.ReasonPhrase;
                return (true, (int)response.StatusCode, ms, null, detail);
            }
            return (true, (int)response.StatusCode, ms, await response.Content.ReadFromJsonAsync<WhoAmIResponse>(JsonOptions, cancellationToken), null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            return (false, null, null, null, e.Message);
        }
    }

    private static async Task<string> ServerVersionAsync(IHttpClientFactory factory, string address, CancellationToken cancellationToken)
    {
        try
        {
            using var client = factory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var response = await client.GetAsync(new Uri(new Uri(address), "api/health/version"), cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var version = await response.Content.ReadFromJsonAsync<ServerVersion>(JsonOptions, cancellationToken);
            return version?.InformationalVersion ?? version?.Version;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>The <c>detail</c> of a problem+json answer, or null.</summary>
    private static string DetailOf(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String ? detail.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Normalize(string address)
        => Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri.AbsoluteUri.TrimEnd('/') + "/" : null;

    private sealed record ServerVersion(string Version, string InformationalVersion);
}
