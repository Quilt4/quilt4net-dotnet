using Quilt4Net.Toolkit.Framework;

namespace Quilt4Net.Toolkit.Api;

/// <summary>
/// Configuration for API calls.
/// This option can be configured by code or with appsettings.json on location "Quilt4Net/ApiLogging"
/// </summary>
public record LoggingOptions
{
    public LoggingOptions()
    {
        // Default interceptor masks the configured SensitiveHeaders on the logged request/response.
        // Set in the constructor (not an initializer) so it can read this instance's SensitiveHeaders
        // at invocation time. Set Interceptor = null to log everything verbatim, or assign your own.
        Interceptor = MaskSensitiveHeadersInterceptor;
    }

    /// <summary>
    /// Add logger for Http request and response with body, headers, query and results.
    /// Default is append to Application Insights requests (customDimensions on AppRequests).
    /// That needs something recording the ASP.NET Core request with a connection string: Application Insights
    /// (<c>AddApplicationInsightsTelemetry</c>) or the Azure Monitor OpenTelemetry exporter.
    /// <see cref="HttpRequestLogMode.Logger"/> writes to AppTraces through <c>ILogger</c> instead.
    /// </summary>
    public HttpRequestLogMode LogHttpRequest { get; set; } = HttpRequestLogMode.ApplicationInsights;

    /// <summary>
    /// If this is added calls to the API picks up 'X-Correlation-ID' from the header and append that to logging on the server.
    /// If there is no CorrelationId provided, one is added and returned with the response to the client.
    /// If scoped, the CorrelationId can be added by...
    /// On the client side use ...
    /// </summary>
    public bool UseCorrelationId { get; set; } = true;

    /// <summary>
    /// Monitor name used to identify log items from this Quilt4Net instrumentation.
    /// </summary>
    /// <remarks>
    /// Moved to <c>Quilt4NetLoggingOptions.MonitorName</c> so the value is attached to every
    /// trace, exception and request via the OTel processors registered by
    /// <c>AddQuilt4NetLogging</c>. Setting it here has no effect.
    /// </remarks>
    [Obsolete("Set MonitorName on Quilt4NetLoggingOptions (AddQuilt4NetLogging(o => o.MonitorName = ...)) instead. The value is now attached as customDimensions[\"quilt4net.monitor\"] on every record, not just AppRequests.")]
    public string MonitorName { get; set; } = Constants.Monitor;

    /// <summary>
    /// The maximum size of the body (request and response) to be logged.
    /// Default is 1 MB.
    /// If set to 0, the body is not logged.
    /// </summary>
    public long MaxBodySize { get; set; } = Constants.MaxBodySize;

    /// <summary>
    /// List of paths to include for logging.
    /// Default is to include all paths that starts with "/Api".
    /// All values are case-insensitive.
    /// To include all paths provide the value ".*".
    /// </summary>
    public string[] IncludePaths { get; set; } = ["^/Api"];

    /// <summary>
    /// Default configuration that specifies whether the request should be logged or not.
    /// Use the LoggingAttribute to override this behavior on individual endpoints.
    /// By default, request body is logged.
    /// </summary>
    public bool LogRequestBodyByDefault { get; set; } = true;

    /// <summary>
    /// Default configuration that specifies whether the response should be logged or not.
    /// Use the LoggingAttribute to override this behavior on individual endpoints.
    /// By default, response body is not logged.
    /// </summary>
    public bool LogResponseBodyByDefault { get; set; } = false;

    /// <summary>Placeholder written in place of a sensitive header's value by the default interceptor.</summary>
    public const string HeaderMask = "***";

    /// <summary>
    /// Header names whose values the default interceptor (<see cref="MaskSensitiveHeadersInterceptor"/>)
    /// masks. Matching is case-insensitive. Defaults to common credential-bearing headers; replace or
    /// extend to suit. Configurable via appsettings at <c>Quilt4Net:ApiLogging:SensitiveHeaders</c>.
    /// Has no effect if you replace <see cref="Interceptor"/> with your own (or set it to <c>null</c>).
    /// </summary>
    public string[] SensitiveHeaders { get; set; } =
        ["Authorization", "X-API-KEY", "Proxy-Authorization", "Cookie", "Set-Cookie"];

    /// <summary>
    /// Modifies or filters the captured request/response before it is logged — the single hook for
    /// removing secrets (headers, body, …). Runs on the built-in logging path.
    /// <list type="bullet">
    /// <item>Default: <see cref="MaskSensitiveHeadersInterceptor"/> — masks the values of <see cref="SensitiveHeaders"/>.</item>
    /// <item><c>null</c>: no filtering — request/response are logged verbatim.</item>
    /// <item>Custom: full control; call <see cref="MaskSensitiveHeadersInterceptor"/> yourself to keep header masking.</item>
    /// </list>
    /// </summary>
    public Func<Request, Response, Dictionary<string, string>, IServiceProvider, Task<(Request, Response, Dictionary<string, string>)>> Interceptor;

    /// <summary>
    /// Default <see cref="Interceptor"/>: masks the values of <see cref="SensitiveHeaders"/> on both
    /// request and response (case-insensitive name match), replacing each with <see cref="HeaderMask"/>
    /// while keeping the key — so a header's presence stays visible without leaking its value. Empty
    /// header values are dropped. Body and details pass through unchanged.
    /// </summary>
    public Task<(Request, Response, Dictionary<string, string>)> MaskSensitiveHeadersInterceptor(
        Request request, Response response, Dictionary<string, string> details, IServiceProvider serviceProvider)
    {
        var sensitive = new HashSet<string>(SensitiveHeaders ?? [], StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string> Mask(Dictionary<string, string> headers) => headers
            .Where(x => !string.IsNullOrEmpty(x.Value))
            .ToDictionary(x => x.Key, x => sensitive.Contains(x.Key) ? HeaderMask : x.Value);

        request = request with { Headers = Mask(request.Headers) };
        if (response != null) response = response with { Headers = Mask(response.Headers) };

        return Task.FromResult((request, response, details));
    }
}