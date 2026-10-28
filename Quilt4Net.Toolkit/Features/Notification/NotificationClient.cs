using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Quilt4Net.Toolkit.Features.Notification;

internal class NotificationClient : INotificationClient
{
    public const string HttpClientName = "Quilt4Net.Notification";

    private const string Root = "Api/Notification";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NotificationOptions _options;
    private readonly IHostEnvironment _hostEnvironment;

    public NotificationClient(IHttpClientFactory httpClientFactory, IOptions<NotificationOptions> options, IHostEnvironment hostEnvironment = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _hostEnvironment = hostEnvironment;
    }

    public Task<NotificationResponse> SendAsync(string category, string message, CancellationToken cancellationToken = default)
        => SendAsync(new SendNotificationRequest { Category = category, Message = message }, cancellationToken);

    public async Task<NotificationResponse> SendAsync(SendNotificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = request with
        {
            Application = request.Application ?? _options.Application ?? Assembly.GetEntryAssembly()?.GetName().Name,
            Environment = request.Environment ?? _options.Environment ?? _hostEnvironment?.EnvironmentName,
            Instance = request.Instance ?? System.Environment.MachineName
        };

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsJsonAsync(Root, body, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new NotificationClientException(
                $"Quilt4Net notification request failed with {(int)response.StatusCode}: {(string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase : detail.Trim())}",
                (int)response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<NotificationResponse>(JsonOptions, cancellationToken);
    }

    public async Task<string[]> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync("Api/Monitoring/categories", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var categories = await response.Content.ReadFromJsonAsync<CategoryItem[]>(JsonOptions, cancellationToken);
            return categories?.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private sealed record CategoryItem(string Name);
}
