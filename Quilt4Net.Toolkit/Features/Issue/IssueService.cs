using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Quilt4Net.Toolkit.Framework;

namespace Quilt4Net.Toolkit.Features.Issue;

internal class IssueService : IIssueService
{
    public const string HttpClientName = "Quilt4Net.Issue";

    private const string Root = "Api/Issue";

    /// <summary>
    /// Enums travel as names, not numbers, for the same reason they are stored as names: an ordinal
    /// is an accident of declaration order, and inserting a member silently re-grades every value
    /// already in flight.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public IssueService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IssueResponse[]> GetAsync(CancellationToken cancellationToken = default)
        => await SendAsync<IssueResponse[]>(HttpMethod.Get, Root, null, cancellationToken);

    public async Task<IssueResponse> GetAsync(int number, CancellationToken cancellationToken = default)
        => await SendAsync<IssueResponse>(HttpMethod.Get, $"{Root}/{number}", null, cancellationToken, allowNotFound: true);

    public async Task<RoadmapResponse> GetRoadmapAsync(CancellationToken cancellationToken = default)
        => await SendAsync<RoadmapResponse>(HttpMethod.Get, $"{Root}/roadmap", null, cancellationToken);

    public async Task<IssueWorkflowResponse> GetWorkflowAsync(CancellationToken cancellationToken = default)
        => await SendAsync<IssueWorkflowResponse>(HttpMethod.Get, $"{Root}/workflow", null, cancellationToken);

    public async Task<IssueResponse> CreateAsync(CreateIssueRequest request, CancellationToken cancellationToken = default)
        => await SendAsync<IssueResponse>(HttpMethod.Post, Root, request, cancellationToken);

    public async Task<IssueResponse> UpdateAsync(int number, UpdateIssueRequest request, CancellationToken cancellationToken = default)
        => await SendAsync<IssueResponse>(HttpMethod.Put, $"{Root}/{number}", request, cancellationToken);

    public async Task<IssueResponse> SetStateAsync(int number, SetIssueStateRequest request, CancellationToken cancellationToken = default)
        => await SendAsync<IssueResponse>(HttpMethod.Post, $"{Root}/{number}/state", request, cancellationToken);

    public async Task<IssueResponse> AddLinkAsync(int number, AddIssueLinkRequest request, CancellationToken cancellationToken = default)
        => await SendAsync<IssueResponse>(HttpMethod.Post, $"{Root}/{number}/link", request, cancellationToken);

    public async Task<IssueResponse> RemoveLinkAsync(int number, int targetNumber, CancellationToken cancellationToken = default)
        => await SendAsync<IssueResponse>(HttpMethod.Delete, $"{Root}/{number}/link/{targetNumber}", null, cancellationToken);

    public async Task DeleteAsync(int number, CancellationToken cancellationToken = default)
        => await SendAsync<object>(HttpMethod.Delete, $"{Root}/{number}", null, cancellationToken, expectContent: false);

    public async Task<IssueWorkflowResponse> SetWorkflowAsync(SetIssueWorkflowRequest request, CancellationToken cancellationToken = default)
        => await SendAsync<IssueWorkflowResponse>(HttpMethod.Put, $"{Root}/workflow", request, cancellationToken);

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object body, CancellationToken cancellationToken, bool allowNotFound = false, bool expectContent = true)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body != null) request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return default;

        if (!response.IsSuccessStatusCode) throw await BuildExceptionAsync(response, cancellationToken);

        if (!expectContent) return default;

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private static async Task<IssueServiceException> BuildExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : ProblemResponse.DetailOf(body) ?? body.Trim();
        return new IssueServiceException(
            $"Quilt4Net issue request failed with {(int)response.StatusCode}: {detail}",
            (int)response.StatusCode,
            RetryAfterPolicy.Of(response));
    }
}
