using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Features.Measure;
using Quilt4Net.Toolkit.Framework;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Quilt4Net.Toolkit.Api.Framework;

public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceProvider _serviceProvider;
    private readonly CompiledLoggingOptions _compiledLoggingOptions;
    private readonly LoggingOptions _options;
    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;

    public RequestResponseLoggingMiddleware(RequestDelegate next, IServiceProvider serviceProvider, CompiledLoggingOptions compiledLoggingOptions, IOptions<LoggingOptions> options, ILogger<RequestResponseLoggingMiddleware> logger)
    {
        _next = next;
        _serviceProvider = serviceProvider;
        _compiledLoggingOptions = compiledLoggingOptions;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var loggingAttr = endpoint?.Metadata.GetMetadata<LoggingAttribute>();
        loggingAttr ??= endpoint?.Metadata.GetMetadata<LoggingStreamAttribute>() == null ? null : new LoggingAttribute { ResponseBody = false };

        //NOTE: Behaviour when logging attribute has not been set.
        var logResponseBody = _options.LogResponseBodyByDefault;
        if (loggingAttr == null)
        {
            var logThisPath = _compiledLoggingOptions.IncludePathRegex.Any(regex => regex.IsMatch(context.Request.Path));
            if (!logThisPath)
            {
                await _next(context);
                return;
            }

            var methodInfo = (endpoint as RouteEndpoint)?.Metadata.OfType<ControllerActionDescriptor>().FirstOrDefault()?.MethodInfo;
            if (methodInfo?.ReturnType == typeof(Task)) logResponseBody = false;
        }

        //NOTE: Do not log when logging has been manually disabled:
        var enableLogging = loggingAttr?.Enabled ?? true;
        if (!enableLogging)
        {
            await _next(context);
            return;
        }

        // Use flags to control what is logged
        var logRequestBody = loggingAttr?.RequestBody ?? _options.LogRequestBodyByDefault;
        logResponseBody = loggingAttr?.ResponseBody ?? logResponseBody;

        context.Request.EnableBuffering();
        var requestDetails = await CaptureRequestDetailsAsync(context, logRequestBody, _options?.MaxBodySize ?? Constants.MaxBodySize);
        var originalResponseBodyStream = context.Response.Body;
        var correlationId = context.Items.TryGetValue("CorrelationId", out var c) ? c?.ToString() : null;
        var details = BuildDetails();

        var sw = new Stopwatch();
        var target = GetRequestTarget(context);

        try
        {
            using var responseBodyStream = new MemoryStream();
            if (logResponseBody)
            {
                context.Response.Body = responseBodyStream;
            }

            if (target != null)
            {
                target.Set("UserId", context.User.Identity?.Name ?? "Anonymous");
                if (!string.IsNullOrEmpty(correlationId)) target.Set("CorrelationId", correlationId);
                // ApplicationName + Version intentionally not duplicated here — AddQuilt4NetLogging
                // attaches service.name / service.version to every record/span via OTel processors,
                // which the Azure Monitor exporter forwards into customDimensions for AppRequests too.
            }

            sw.Start();
            await _next(context);
            sw.Stop();

            var responseDetails = await CaptureResponseDetailsAsync(context, logResponseBody, _options?.MaxBodySize ?? Constants.MaxBodySize);

            //NOTE: Intercept the request, response and details, so that it can be modified and cleaned
            // from secrets. The interceptor defaults to LoggingOptions.MaskSensitiveHeadersInterceptor
            // (masks credential-bearing header values); set Interceptor = null to log verbatim, or
            // supply your own. When null, headers/body are logged exactly as captured.
            if (_options?.Interceptor != null)
            {
                try
                {
                    (requestDetails, responseDetails, details) = await _options.Interceptor.Invoke(requestDetails, responseDetails, details, _serviceProvider);
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException($"Log {nameof(_options.Interceptor)} Exception. {e.Message}", e);
                }
            }

            var detailsJsonString = BuildDetailsString(details);
            if (target != null)
            {
                target.Set("Request", System.Text.Json.JsonSerializer.Serialize(requestDetails));
                target.Set("Response", System.Text.Json.JsonSerializer.Serialize(responseDetails));
                target.Set("Elapsed", $"{sw.Elapsed}");
                target.Set("Details", detailsJsonString);
            }

            await LogRequestAndResponseAsync(requestDetails, responseDetails, sw.Elapsed, correlationId, detailsJsonString);

            if (logResponseBody)
            {
                await responseBodyStream.CopyToAsync(originalResponseBodyStream);
            }
        }
        catch (Exception e)
        {
            var detailsJsonString = BuildDetailsString(details, e);
            if (target != null)
            {
                target.Set("ExceptionMessage", e.Message);
                target.Set("StackTrace", e.InnerException?.StackTrace ?? e.StackTrace);
                target.Set("Elapsed", $"{sw.Elapsed}");
                target.Set("Details", detailsJsonString);
                target.Fail(e.Message);
            }

            await LogRequestAndResponseAsync(requestDetails, null, sw.Elapsed, correlationId, detailsJsonString, e);
            throw;
        }
        finally
        {
            context.Response.Body = originalResponseBodyStream;
        }
    }

    private RequestTarget GetRequestTarget(HttpContext context)
    {
        if (!(_options?.LogHttpRequest.HasFlag(HttpRequestLogMode.ApplicationInsights) ?? false)) return null;

        //NOTE: Application Insights SDK 2.x put a RequestTelemetry on the HttpContext. SDK 3.x is built on
        // OpenTelemetry and does not, so the request span is the row to write to. Its tags are exported into
        // customDimensions on AppRequests by both the 3.x SDK and the Azure Monitor exporter.
        var telemetry = context.Features.Get<RequestTelemetry>();
        var activity = context.Features.Get<IHttpActivityFeature>()?.Activity ?? Activity.Current;
        if (telemetry == null && activity == null)
        {
            //NOTE: The path is caller supplied. Line breaks are stripped so it cannot forge a second log line.
            var path = context.Request.Path.Value?.Replace("\r", string.Empty).Replace("\n", string.Empty);
            _logger.LogDebug("No request telemetry or request activity for {Path}, so request data cannot be attached to AppRequests.", path);
            return null;
        }

        return new RequestTarget(telemetry, activity);
    }

    internal sealed class RequestTarget
    {
        private readonly RequestTelemetry _telemetry;
        private readonly Activity _activity;

        public RequestTarget(RequestTelemetry telemetry, Activity activity)
        {
            _telemetry = telemetry;
            _activity = activity;
        }

        public void Set(string key, string value)
        {
            if (_telemetry != null) _telemetry.Properties[key] = value;
            _activity?.SetTag(key, value);
        }

        public void Fail(string description)
        {
            if (_telemetry != null) _telemetry.Success = false;
            _activity?.SetStatus(ActivityStatusCode.Error, description);
        }
    }

    private async Task<Request> CaptureRequestDetailsAsync(HttpContext context, bool logBody, long maxBodySize)
    {
        if (maxBodySize == 0) logBody = false;

        var headers = BuildHeaders(context.Request.Headers);
        var query = BuildQueries(context.Request.Query);

        var body = "[Not logged]";
        if (logBody)
        {
            context.Request.EnableBuffering();

            if (context.Request.ContentLength.HasValue && context.Request.ContentLength <= maxBodySize)
            {
                body = await ReadRequestBodyAsync(context.Request);
            }
            else if (!context.Request.ContentLength.HasValue)
            {
                // Chunked/streamed request (no Content-Length): buffer once to measure size, then
                // decode that same buffer — avoids a second full read of the request body. The body
                // is rewound (Position = 0 below) so the downstream handler still reads it normally.
                using var memStream = new MemoryStream();
                await context.Request.Body.CopyToAsync(memStream);
                body = memStream.Length <= maxBodySize
                    ? Encoding.UTF8.GetString(memStream.GetBuffer(), 0, (int)memStream.Length)
                    : $"[Skipped: Request body exceeds {maxBodySize} bytes]";
            }
            else
            {
                body = $"[Skipped: Request body exceeds {maxBodySize} bytes]";
            }

            context.Request.Body.Position = 0;
        }

        return new Request
        {
            Method = context.Request.Method,
            Path = context.Request.Path,
            Headers = headers.ToUniqueDictionary(),
            Query = query.ToUniqueDictionary(),
            Body = body,
            ClientIp = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown"
        };
    }

    private async Task<string> ReadRequestBodyAsync(HttpRequest request)
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;
        return body;
    }

    private static IEnumerable<KeyValuePair<string, string>> BuildHeaders(IHeaderDictionary headers)
    {
        foreach (var header in headers)
        {
            yield return new KeyValuePair<string, string>(header.Key, header.Value);
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> BuildQueries(IQueryCollection queries)
    {
        foreach (var querie in queries)
        {
            yield return new KeyValuePair<string, string>(querie.Key, querie.Value);
        }
    }

    private async Task<Response> CaptureResponseDetailsAsync(HttpContext context, bool logBody, long maxBodySize)
    {
        if (maxBodySize == 0) logBody = false;

        var headers = BuildHeaders(context.Response.Headers);
        var body = "[Not logged]";

        if (logBody)
        {
            context.Response.Body.Seek(0, SeekOrigin.Begin);

            using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
            var buffer = new char[maxBodySize + 1];
            var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);

            if (read > maxBodySize)
            {
                body = $"[Skipped: Response body exceeds {maxBodySize} bytes]";
            }
            else
            {
                body = new string(buffer, 0, read);
            }

            context.Response.Body.Seek(0, SeekOrigin.Begin);
        }

        return new Response
        {
            StatusCode = context.Response.StatusCode,
            Headers = headers.ToUniqueDictionary(),
            Body = body,
        };
    }

    private Task LogRequestAndResponseAsync(Request request, Response response, TimeSpan elapsed, string correlationId, string detailsJson, Exception e = null)
    {
        if (!(_options?.LogHttpRequest.HasFlag(HttpRequestLogMode.Logger) ?? false)) return Task.CompletedTask;

        var requestJson = System.Text.Json.JsonSerializer.Serialize(request);
        var responseJson = response != null ? System.Text.Json.JsonSerializer.Serialize(response) : null;

        if (e == null)
        {
            _logger.LogInformation("Http {Method} to {Path} in {Elapsed} ms. {Request} {Response} {Details} CorrelationId: {CorrelationId}", request.Method, request.Path, elapsed, requestJson, responseJson, detailsJson, correlationId);
        }
        else
        {
            _logger.LogError("Http {Method} to {Path} in {Elapsed} ms, failed {ErrorMessage} @{StackTrace}. {Request} {Details} CorrelationId: {CorrelationId}", request.Method, request.Path, elapsed, e.Message, e.StackTrace, requestJson, detailsJson, correlationId);
        }

        return Task.CompletedTask;
    }

    private string BuildDetailsString(Dictionary<string, string> details, Exception e = null)
    {
        if (e != null)
        {
            foreach (var data in e.GetData())
            {
                details.TryAdd(data.Key, $"{data.Value}");
            }
        }

        var json = System.Text.Json.JsonSerializer.Serialize(details);
        return json;
    }

    private Dictionary<string, string> BuildDetails() //Exception e)
    {
        // "Monitor" intentionally omitted — AddQuilt4NetLogging attaches it to every record
        // as customDimensions["quilt4net.monitor"], so it no longer needs to live inside
        // this per-request Details JSON blob.
        var dictionary = new Dictionary<string, string>
        {
            { "Method", "Http" }
        };

        var d = dictionary.Where(x => !string.IsNullOrEmpty(x.Value)).ToUniqueDictionary();
        return d;
    }
}