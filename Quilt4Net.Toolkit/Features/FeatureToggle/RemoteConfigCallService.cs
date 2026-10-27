using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Framework;

namespace Quilt4Net.Toolkit.Features.FeatureToggle;

internal class RemoteConfigCallService : IRemoteConfigCallService, IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _resolutions;
    private readonly Histogram<double> _resolutionDuration;

    /// <summary>Named <see cref="IHttpClientFactory"/> client for config/toggle calls to Quilt4Net.Server.</summary>
    public const string HttpClientName = "Quilt4Net.RemoteConfiguration";

    private readonly IServiceProvider _serviceProvider;
    private readonly EnvironmentName _environmentName;
    private readonly RemoteConfigurationOptions _options;
    private readonly ILogger<RemoteConfigCallService> _logger;
    private readonly ConcurrentDictionary<string, FeatureToggleResponse> _localCache = new();
    private readonly ConcurrentDictionary<string, bool> _refreshInProgress = new();
    private readonly FailureBackoff _failureBackoff = new();

    public RemoteConfigCallService(IServiceProvider serviceProvider, EnvironmentName environmentName, IOptions<RemoteConfigurationOptions> options, ILogger<RemoteConfigCallService> logger)
    {
        _serviceProvider = serviceProvider;
        _environmentName = environmentName;
        _options = options.Value;
        _logger = logger;

        _meter = new Meter(Quilt4NetMetrics.ConfigurationMeterName);
        _resolutions = _meter.CreateCounter<long>(Quilt4NetMetrics.ConfigurationResolutions, "{resolution}",
            "Configuration resolutions, tagged by where the value came from and by key.");
        _resolutionDuration = _meter.CreateHistogram<double>(Quilt4NetMetrics.ConfigurationResolutionDuration, "ms",
            "How long a configuration resolution took, tagged by where the value came from and by key.");
        _meter.CreateObservableGauge(Quilt4NetMetrics.ConfigurationBackoffKeys, () => _failureBackoff.ActiveCount, "{key}",
            "Configuration keys currently held off by the failure back-off.");
    }

    public void Dispose() => _meter.Dispose();

    public async Task<T> MakeCallAsync<T>(string key, T defaultValue, TimeSpan? ttl, string application = null)
    {
        var result = await MakeCallResultAsync(key, defaultValue, ttl, application);
        return result.Value;
    }

    public async Task<ConfigurationResult<T>> MakeCallResultAsync<T>(string key, T defaultValue, TimeSpan? ttl, string application = null)
    {
        ttl ??= _options.Ttl;
        var sw = Stopwatch.StartNew();

        // Resolve the effective application up front so the cache key, the
        // outbound request and the background refresh all use the same value.
        // Same toggle key with different applications is a different cache entry —
        // otherwise the second call returns the first call's value silently.
        var effectiveApplication = ResolveApplication(application);
        var cacheKey = BuildCacheKey(key, effectiveApplication);

        try
        {
            var changeType = ((T)Convert.ChangeType($"{defaultValue}", typeof(T)));
            if (!$"{changeType}".Equals($"{defaultValue}")) throw new NotSupportedException($"Value of type {typeof(T).Name} is not supported.");

            _localCache.TryGetValue(cacheKey, out var cached);
            var needRefresh = cached == null || DateTime.UtcNow > cached.ValidTo;

            // Per-resolution result lines are Debug — they fire on every flag check (typically a
            // 0ms cache hit) and produced ~449k Information traces/week per app in production.
            // Errors / warnings / actual value changes still log at their original levels.
            if (!needRefresh)
            {
                // A negative-cache entry carries no server value, so it resolves to the caller's
                // fallback — reported as Fallback rather than Cache, or a pinned value would read as
                // a genuine cache hit from the second call onwards.
                var source = cached.Value == null ? ConfigurationSource.Fallback : ConfigurationSource.Cache;
                return Resolved(key, GetCachedOrDefault(cached, defaultValue), source, source == ConfigurationSource.Fallback, sw);
            }

            // Stale-while-revalidate: if we have a stale cached value, return it immediately
            // and refresh in the background. Disabled via options → fall through to a synchronous
            // fetch so the caller gets a fresh value.
            if (cached != null && _options.StaleWhileRevalidate)
            {
                StartBackgroundRefresh(key, cacheKey, defaultValue, ttl, effectiveApplication);
                var source = cached.Value == null ? ConfigurationSource.Fallback : ConfigurationSource.StaleCache;
                return Resolved(key, GetCachedOrDefault(cached, defaultValue), source, true, sw);
            }

            // No cache (or stale-while-revalidate disabled) — fetch with timeout; the catch below
            // still falls back to any stale cached value on failure.
            return await FetchWithTimeout(key, cacheKey, defaultValue, ttl, sw, effectiveApplication);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "{Message} Using stale cache or fallback for key {Key}.", e.Message, key);
            if (_localCache.TryGetValue(cacheKey, out var stale))
            {
                CacheFailure(cacheKey, stale);
                var source = stale.Value == null ? ConfigurationSource.Fallback : ConfigurationSource.StaleCache;
                return Resolved(key, GetCachedOrDefault(stale, defaultValue), source, true, sw);
            }
            return Resolved(key, defaultValue, ConfigurationSource.Fallback, true, sw);
        }
    }

    /// <summary>
    /// One place that both logs a resolution and states its provenance, so the two cannot disagree —
    /// the source used to be baked into each log template by hand and reported to no caller at all.
    /// </summary>
    private ConfigurationResult<T> Resolved<T>(string key, T value, ConfigurationSource source, bool stale, Stopwatch sw)
    {
        _logger.LogDebug("Configuration '{Key}' resolved in {Elapsed}ms. Source: {Source}, Stale: {Stale}, Value: '{Value}'.",
            key, sw.ElapsedMilliseconds, source, stale, value);

        if (_options.MetricsEnabled)
        {
            // The key IS a tag here, unlike content: a host has a handful of toggles rather than
            // hundreds of keys, and "which toggle is falling back" is the question an operator asks.
            var tags = new TagList
            {
                { "source", source.ToString() },
                { "key", key },
                { "application", _options.Application ?? "" },
                { "environment", _environmentName.Name ?? "" },
                { "stale", stale },
            };

            _resolutions.Add(1, tags);
            _resolutionDuration.Record(sw.ElapsedMilliseconds, tags);
        }

        return new ConfigurationResult<T> { Value = value, Source = source, Stale = stale };
    }

    private async Task<ConfigurationResult<T>> FetchWithTimeout<T>(string key, string cacheKey, T defaultValue, TimeSpan? ttl, Stopwatch sw, string effectiveApplication)
    {
        try
        {
            var assemblyName = Assembly.GetEntryAssembly()?.GetName();
            var request = new FeatureToggleRequest
            {
                Key = key,
                Application = effectiveApplication,
                Environment = _environmentName.Name,
                Instance = null,
                Version = $"{assemblyName?.Version}",
                DefaultValue = $"{defaultValue}",
                ValueType = typeof(T).Name,
                Ttl = ttl
            };
            var complexKey = BuildKey(request);

            using var cts = new CancellationTokenSource(_options.HttpTimeout);
            using var client = GetHttpClient();
            var address = $"Api/Configuration/{complexKey}";
            var response = await client.GetAsync(address, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                await LogNonSuccessAsync(key, response, background: false);
                CacheFailure(cacheKey, null, RetryAfterPolicy.Of(response));
                return Resolved(key, defaultValue, ConfigurationSource.Fallback, true, sw);
            }

            var result = await response.Content.ReadFromJsonAsync<FeatureToggleResponse>(cancellationToken: cts.Token);

            _failureBackoff.Reset(cacheKey);
            _localCache.AddOrUpdate(cacheKey, result, (_, _) => result);

            // The server answering with no value for the key is still the caller's fallback being
            // used, and that is what a caller asking about provenance needs to know. Not stale
            // though — this is a current answer, it just isn't a value.
            var served = result?.Value == null ? ConfigurationSource.Fallback : ConfigurationSource.Server;
            return Resolved(key, GetCachedOrDefault(result, defaultValue), served, false, sw);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("HTTP request timed out for configuration '{Key}' after {Timeout}ms. Using default value.",
                key, _options.HttpTimeout.TotalMilliseconds);
            CacheFailure(cacheKey, null);
            return Resolved(key, defaultValue, ConfigurationSource.Fallback, true, sw);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "{Message} Using default for key {Key}.", e.Message, key);
            CacheFailure(cacheKey, null);
            return Resolved(key, defaultValue, ConfigurationSource.Fallback, true, sw);
        }
    }

    private void StartBackgroundRefresh<T>(string key, string cacheKey, T defaultValue, TimeSpan? ttl, string effectiveApplication)
    {
        if (!_refreshInProgress.TryAdd(cacheKey, true)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var assemblyName = Assembly.GetEntryAssembly()?.GetName();
                var request = new FeatureToggleRequest
                {
                    Key = key,
                    Application = effectiveApplication,
                    Environment = _environmentName.Name,
                    Instance = null,
                    Version = $"{assemblyName?.Version}",
                    DefaultValue = $"{defaultValue}",
                    ValueType = typeof(T).Name,
                    Ttl = ttl
                };
                var complexKey = BuildKey(request);

                using var cts = new CancellationTokenSource(_options.HttpTimeout);
                using var client = GetHttpClient();
                var address = $"Api/Configuration/{complexKey}";
                var response = await client.GetAsync(address, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    await LogNonSuccessAsync(key, response, background: true);
                    CacheFailure(cacheKey, _localCache.GetValueOrDefault(cacheKey), RetryAfterPolicy.Of(response));
                    return;
                }

                var result = await response.Content.ReadFromJsonAsync<FeatureToggleResponse>(cancellationToken: cts.Token);

                _failureBackoff.Reset(cacheKey);
                _localCache.AddOrUpdate(cacheKey, result, (_, _) => result);
                _logger.LogInformation("Background refresh for '{Key}' completed. New value: '{Value}', ValidTo: {ValidTo}.",
                    key, result.Value, result.ValidTo);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Background refresh for '{Key}' timed out after {Timeout}ms.",
                    key, _options.HttpTimeout.TotalMilliseconds);
                CacheFailure(cacheKey, _localCache.GetValueOrDefault(cacheKey));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Background refresh for '{Key}' failed: {Message}.", key, e.Message);
                CacheFailure(cacheKey, _localCache.GetValueOrDefault(cacheKey));
            }
            finally
            {
                _refreshInProgress.TryRemove(cacheKey, out _);
            }
        });
    }

    private string ResolveApplication(string application)
    {
        // The convention: only null means "default — toolkit looks it up". Empty string is
        // an explicit "shared" and is forwarded as-is. A non-empty value is forwarded as-is.
        if (application != null) return application;
        return _options.Application ?? Assembly.GetEntryAssembly()?.GetName().Name;
    }

    private static string BuildCacheKey(string toggleKey, string effectiveApplication)
    {
        // "" and null both mean shared so they collapse to the same cache slot.
        return $"{toggleKey}|{effectiveApplication ?? ""}";
    }

    public async Task<ConfigurationResponse[]> GetAllAsync()
    {
        try
        {
            using var client = GetHttpClient();
            var response = await client.GetAsync("Api/Configuration");

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("No configuration list: {Detail} Returning empty list.", await ForbiddenDetailAsync(response));
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Unable to get all configuration. Response was {StatusCode} {ReasonPhrase}. Returning empty list.",
                    response.StatusCode, response.ReasonPhrase);
                return [];
            }

            // No client-side filtering: this method backs RemoteConfigurationAdmin's full-list view,
            // which is meant to surface every entry the team's API key can read. Filtering down to
            // the current app + environment hides shared (Application=null) entries and cross-app
            // entries the operator may need to manage. Callers wanting a narrower slice can apply
            // their own Where().
            return await response.Content.ReadFromJsonAsync<ConfigurationResponse[]>() ?? [];
        }
        catch (Exception e)
        {
            _logger.LogError(e, "{Message} Returning empty list.", e.Message);
            return [];
        }
    }

    public async Task DeleteAsync(string key, string application, string environment, string instance)
    {
        var requestBody = new
        {
            Key = key,
            Application = application,
            Environment = environment,
            Instance = instance,
        };

        using var client = GetHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Delete, "Api/Configuration");
        request.Content = JsonContent.Create(requestBody);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetValueAsync(string key, string application, string environment, string instance, string value)
    {
        var requestBody = new
        {
            Key = key,
            Application = application,
            Environment = environment,
            Instance = instance,
            Value = value
        };

        using var client = GetHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, "Api/Configuration");
        request.Content = JsonContent.Create(requestBody);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static T GetCachedOrDefault<T>(FeatureToggleResponse cached, T defaultValue)
    {
        if (cached?.Value == null) return defaultValue;
        try
        {
            return (T)Convert.ChangeType(cached.Value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Negative-cache after a failed call, for the back-off interval rather than a full cache
    /// lifetime.
    /// </summary>
    /// <remarks>
    /// This method is the whole of issue #174. It used to stamp <c>ValidTo</c> a complete TTL into
    /// the future — the last <b>successful</b> response's TTL at that, so the configured duration
    /// was unreachable for any key that had ever worked. One failed call therefore bought ten
    /// minutes of the fallback value, the next expiry landed on another failed call, and the toggle
    /// had no path back to the server's value while the fault lasted.
    /// </remarks>
    /// <summary>
    /// Logs a non-success response, distinguishing backpressure from a fault.
    /// </summary>
    /// <remarks>
    /// A <c>429</c> is the server applying backpressure — designed behaviour on both sides, not an
    /// outage — and under load it is the single most repeated line there is. Logging it at Error
    /// makes a healthy shed look like a failure and buries the errors that are real. The content
    /// client has drawn this distinction since 1.0.6; configuration had no 429 handling at all until
    /// the server began shedding per caller.
    /// <para>
    /// A <c>403</c> is a warning too, carrying the server's own sentence: the key lacks
    /// <c>config:read</c>, or the team has switched Remote configuration off - a choice, after which
    /// every read falls back to its default by design.
    /// </para>
    /// </remarks>
    private async Task LogNonSuccessAsync(string key, HttpResponseMessage response, bool background)
    {
        var what = background ? "Background refresh for" : "Unable to get feature toggle for key";

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("{What} '{Key}' was rate limited (429). Backing off for {RetryAfter}.",
                what, key, RetryAfterPolicy.Of(response)?.ToString() ?? "the configured failure duration");
            return;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("{What} '{Key}' was refused (403): {Detail} Using the default value.",
                what, key, await ForbiddenDetailAsync(response));
            return;
        }

        _logger.LogError("{What} '{Key}' failed. Response was {StatusCode} {ReasonPhrase}.",
            what, key, response.StatusCode, response.ReasonPhrase);
    }

    private static async Task<string> ForbiddenDetailAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return ProblemResponse.DetailOf(body) ?? response.ReasonPhrase;
    }

    /// <param name="overrideDuration">
    /// A <c>429</c>'s <c>Retry-After</c>, which wins over the local back-off: the server stating when
    /// it will be ready beats any client-side guess, in both directions.
    /// </param>
    private void CacheFailure(string cacheKey, FeatureToggleResponse stale, TimeSpan? overrideDuration = null)
    {
        var duration = overrideDuration
                       ?? _failureBackoff.Next(cacheKey, _options.FailureCacheDuration, _options.MaxFailureCacheDuration);
        var failureResponse = new FeatureToggleResponse
        {
            Value = stale?.Value,
            ValidTo = DateTime.UtcNow.Add(duration)
        };
        _localCache.AddOrUpdate(cacheKey, failureResponse, (_, _) => failureResponse);
    }

    private static string BuildKey(FeatureToggleRequest request)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(request);
        var bytes = Encoding.UTF8.GetBytes(json);
        var base64 = Convert.ToBase64String(bytes);
        var payload = WebUtility.UrlEncode(base64);
        return payload;
    }

    // Factory-created named client (BaseAddress + X-API-KEY + correlation handler configured at
    // registration). Resolved via the existing IServiceProvider so no constructor change is needed.
    private HttpClient GetHttpClient()
        => _serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
}
