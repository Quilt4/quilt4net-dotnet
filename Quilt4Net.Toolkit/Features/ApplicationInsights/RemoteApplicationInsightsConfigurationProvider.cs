using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quilt4Net.Toolkit.Framework;

namespace Quilt4Net.Toolkit.Features.ApplicationInsights;

internal class RemoteApplicationInsightsConfigurationProvider : IApplicationInsightsConfigurationProvider
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly RemoteConfigurationOptions _options;
    private readonly ILogger<RemoteApplicationInsightsConfigurationProvider> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private ApplicationInsightsConfigurationResponse[] _cached;
    private DateTime _cachedAt = DateTime.MinValue;
    private volatile bool _refreshInProgress;

    public RemoteApplicationInsightsConfigurationProvider(
        IOptions<RemoteConfigurationOptions> options,
        ILogger<RemoteApplicationInsightsConfigurationProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ApplicationInsightsConfigurationResponse[]> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var ttl = _options.Ttl ?? DefaultTtl;
        var cached = _cached;
        var age = DateTime.UtcNow - _cachedAt;

        if (cached != null && age < ttl) return cached;

        // Stale-while-revalidate: if we have any cached value, return it immediately and refresh in the background.
        if (cached != null)
        {
            StartBackgroundRefresh();
            return cached;
        }

        // No cache yet — must wait for the first fetch.
        return await FetchAsync(cancellationToken);
    }

    private async Task<ApplicationInsightsConfigurationResponse[]> FetchAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have populated the cache while we waited.
            var cached = _cached;
            var ttl = _options.Ttl ?? DefaultTtl;
            if (cached != null && DateTime.UtcNow - _cachedAt < ttl) return cached;

            using var timeoutCts = new CancellationTokenSource(_options.HttpTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            using var client = CreateHttpClient();

            var response = await client.GetAsync("Api/Monitoring/ApplicationInsights", linkedCts.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // The key lacks logging:read (it was monitor:read), or the team has switched Logging off on
                // the server - a choice, so a warning with the server's own sentence rather than an error.
                var detail = ProblemResponse.DetailOf(await response.Content.ReadAsStringAsync(linkedCts.Token)) ?? response.ReasonPhrase;
                _logger.LogWarning("No Application Insights configurations: {Detail}", detail);
                return cached ?? [];
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Unable to get Application Insights configurations. Response was {StatusCode} {ReasonPhrase}.",
                    response.StatusCode, response.ReasonPhrase);
                return cached ?? [];
            }

            var data = await response.Content.ReadFromJsonAsync<ApplicationInsightsConfigurationResponse[]>(linkedCts.Token)
                       ?? [];

            _cached = data;
            _cachedAt = DateTime.UtcNow;
            return data;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "HTTP request for Application Insights configurations timed out after {Timeout}ms.",
                _options.HttpTimeout.TotalMilliseconds);
            return _cached ?? [];
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to fetch Application Insights configurations: {Message}.", e.Message);
            return _cached ?? [];
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void StartBackgroundRefresh()
    {
        if (_refreshInProgress) return;
        _refreshInProgress = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await FetchAsync(CancellationToken.None);
            }
            finally
            {
                _refreshInProgress = false;
            }
        });
    }

    private HttpClient CreateHttpClient()
    {
        HttpClient client = null;
        try
        {
            client = new HttpClient { BaseAddress = new Uri(_options.Quilt4NetAddress) };
            client.DefaultRequestHeaders.Add("X-API-KEY", _options.ApiKey);
            client.DefaultRequestHeaders.Remove(Quilt4NetClient.HeaderName);
            client.DefaultRequestHeaders.Add(Quilt4NetClient.HeaderName, Quilt4NetClient.Value);
            return client;
        }
        catch
        {
            client?.Dispose();
            throw;
        }
    }
}
