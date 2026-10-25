using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Framework;

namespace Quilt4Net.Toolkit.Features.ReleaseNotes;

/// <summary>
/// HTTP wire for <see cref="IReleaseNoteService"/>. Reuses the named HttpClient, API key and
/// application name that content reads already use — one connection to Quilt4Net.Server, configured
/// once.
/// </summary>
/// <remarks>
/// Degrades quietly, like the other read paths: a page that cannot show "what's new" is a page
/// missing a nicety, and throwing at a consumer mid-render would make release notes more disruptive
/// than the news they carry.
/// </remarks>
internal sealed class RemoteReleaseNoteService : IReleaseNoteService
{
    private readonly ContentOptions _contentOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RemoteReleaseNoteService> _logger;

    public RemoteReleaseNoteService(IOptions<ContentOptions> contentOptions, IHttpClientFactory httpClientFactory, ILogger<RemoteReleaseNoteService> logger)
    {
        _contentOptions = contentOptions.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ReleaseNoteDto> GetAsync(string version, string application = null, CancellationToken cancellationToken = default)
    {
        if (!HasApiKey()) return null;

        if (!SemanticVersion.TryParse(version, out var parsed))
        {
            _logger?.LogWarning("Release note requested for '{Version}', which is not a SemVer 2.0 version. No request was made.", version);
            return null;
        }

        var query = $"application={WebUtility.UrlEncode(ResolveApplication(application))}&version={WebUtility.UrlEncode(parsed.Original)}";
        return await GetAsync<ReleaseNoteDto>($"Api/ReleaseNote?{query}", $"release note {parsed.Original}", cancellationToken);
    }

    public async Task<ReleaseNoteDeltaDto> GetDeltaAsync(string sinceVersion, bool includePreRelease = false, string application = null, CancellationToken cancellationToken = default)
    {
        if (!HasApiKey()) return ReleaseNoteDeltaDto.Empty;

        // No start version, no call. A first-ever visitor has nothing to catch up on, and the
        // alternative reading — "send everything" — is how this endpoint would turn into a scan of a
        // team's entire release history. The server refuses the same call with a 400; short-circuiting
        // here means the common case costs nothing rather than a round trip to be told no.
        if (string.IsNullOrWhiteSpace(sinceVersion))
        {
            _logger?.LogDebug("Release note delta asked for with no version to start from. No request was made.");
            return ReleaseNoteDeltaDto.Empty;
        }

        if (!SemanticVersion.TryParse(sinceVersion, out var parsed))
        {
            // Logged louder than the empty case on purpose: an unparsable version is a caller bug
            // (a 'v' prefix, a four-part assembly version), and returning an empty delta for it looks
            // exactly like "you are up to date".
            _logger?.LogWarning("Release note delta asked for since '{SinceVersion}', which is not a SemVer 2.0 version. No request was made, and no news will be shown.", sinceVersion);
            return ReleaseNoteDeltaDto.Empty;
        }

        var query = $"application={WebUtility.UrlEncode(ResolveApplication(application))}&sinceVersion={WebUtility.UrlEncode(parsed.Original)}&includePreRelease={(includePreRelease ? "true" : "false")}";
        return await GetAsync<ReleaseNoteDeltaDto>($"Api/ReleaseNote/delta?{query}", $"release notes since {parsed.Original}", cancellationToken) ?? ReleaseNoteDeltaDto.Empty;
    }

    private async Task<T> GetAsync<T>(string route, string what, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_contentOptions.HttpTimeout);
            using var client = _httpClientFactory.CreateClient(Features.Content.RemoteContentCallService.HttpClientName);

            var response = await client.GetAsync(route, cts.Token);

            if (response.StatusCode == HttpStatusCode.NotFound) return null;

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                // The key lacks release:read, or the team has switched Documentation off - a choice,
                // not a fault, so a warning rather than an error on every page that asks. The server's
                // sentence says which.
                var detail = ProblemResponse.DetailOf(await response.Content.ReadAsStringAsync(cts.Token)) ?? response.ReasonPhrase;
                _logger?.LogWarning("No {What}: {Detail}", what, detail);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogError("Failed to fetch {What}. Response was {StatusCode} {ReasonPhrase}.", what, response.StatusCode, response.ReasonPhrase);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogWarning("HTTP request timed out fetching {What} after {Timeout}ms.", what, _contentOptions.HttpTimeout.TotalMilliseconds);
            return null;
        }
        catch (Exception e)
        {
            _logger?.LogError(e, "{Message} No release notes will be shown.", e.Message);
            return null;
        }
    }

    private bool HasApiKey()
    {
        if (!string.IsNullOrEmpty(_contentOptions.ApiKey)) return true;

        // Same posture as the other readers: no key means no remote calls at all, and the consumer
        // renders its page without news rather than paying for a round trip that cannot succeed.
        _logger?.LogDebug("No Quilt4Net api key is configured, so no release notes were requested.");
        return false;
    }

    private string ResolveApplication(string application)
        => application ?? _contentOptions.Application ?? Assembly.GetEntryAssembly()?.GetName()?.Name;
}
