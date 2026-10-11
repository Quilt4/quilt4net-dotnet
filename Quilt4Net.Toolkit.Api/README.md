# Quilt4Net.Toolkit.Api
[![GitHub repo](https://img.shields.io/github/repo-size/Quilt4/Quilt4Net.Toolkit?style=flat&logo=github&logoColor=red&label=Repo)](https://github.com/Quilt4/Quilt4Net.Toolkit)

HTTP request/response logging and correlation tracking middleware for .NET Web Applications.

Captures method, path, headers, query parameters, request/response bodies, client IP, and execution time. Logs to Application Insights and/or standard `ILogger`.

## Get started

Install the NuGet package [Quilt4Net.Toolkit.Api](https://www.nuget.org/packages/Quilt4Net.Toolkit.Api) and register the service in `Program.cs`.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddQuilt4NetLogging()
    .AddHttpRequestLogging();

var app = builder.Build();

app.UseQuilt4NetLogging();

app.Run();
```

`AddQuilt4NetLogging()` configures OpenTelemetry resource attributes and registers per-record processors that tag every `AppTrace` / `AppException` / `AppRequest` with `service.name`, `service.version`, `host.name`, `deployment.environment`, and `quilt4net.monitor` (see the core `Quilt4Net.Toolkit` README for details). `AddHttpRequestLogging()` opts in to HTTP request/response middleware on top of that. By default, all requests to paths starting with `/Api` are logged.

> The old `AddQuilt4NetApiLogging()` and `UseQuilt4NetApiLogging()` methods still work but are deprecated.

## Correlation ID

`CorrelationIdMiddleware` reads the `X-Correlation-ID` header from incoming requests; if no header is present, a new GUID is generated. The id is:

1. Stored in `HttpContext.Items["CorrelationId"]` for inspection by downstream code.
2. Returned in the response's `X-Correlation-ID` header — clients can chain the same id to subsequent requests for distributed tracing.
3. **Pushed into a logging scope** (`Logger.BeginScope({ ["CorrelationId"] = id })`) for the duration of the request, so every `ILogger` call made while handling the request inherits the id as a structured property.

For the scoped id to reach `customDimensions["CorrelationId"]` on the resulting `AppTrace` / `AppException` / `AppRequest` rows, enable scope capture — scope values are not exported by default:

```csharp
builder.AddQuilt4NetLogging(o => o.IncludeScopes = true)
    .AddHttpRequestLogging();
```

(See the base `Quilt4Net.Toolkit` README → "Exception data and log scopes".)

KQL pattern for "show me everything from one call chain":

```kql
union AppTraces, AppExceptions, AppRequests
| where Properties contains "<your-correlation-id>"
| order by TimeGenerated asc
```

Or, in the toolkit's `LogView` Search tab, paste the id into the search box (it filters on both `Message` and `CorrelationId`). The Search tab's CorrelationId column also has a click-to-self-search shortcut — click any row's correlation chip and the grid re-runs scoped to that id.

### Forwarding the correlation id on outbound calls

The middleware gives the *current* request an id. To keep that id flowing when your app calls
**other services** over HTTP — so one id spans your app, the services it calls, and Quilt4Net.Server —
opt the relevant `HttpClient`s into propagation:

```csharp
// One-time registration (implied by AddQuilt4NetLogging().AddHttpRequestLogging(), but safe to call directly):
builder.Services.AddQuilt4NetCorrelationId();

// Opt in each client that calls a correlation-aware (typically your own internal) service:
builder.Services.AddHttpClient("internal-api")
    .AddQuilt4NetCorrelationId();
```

Any request sent through that client carries the current request's `X-Correlation-ID`. If the
receiving service also runs `CorrelationIdMiddleware`, it continues the same id instead of minting a
new one — so a single id ties the whole chain together in Application Insights.

Notes:
- **Opt-in per client by design.** Don't attach it to clients calling third-party APIs (Fortnox,
  Stripe, Azure, …) — leaking an internal id to endpoints that don't read it is pointless and noisy.
- **No ambient id → no header.** Outside a request (background work, non-ASP.NET hosts) nothing is
  added; an explicitly-set `X-Correlation-ID` on the request is never overwritten.
- Quilt4Net.Toolkit's own clients (`Content`, `RemoteConfiguration`/feature toggles, `ValueGroup`)
  already forward the id to Quilt4Net.Server automatically once `AddHttpRequestLogging()` is wired up.

## Logging mode

Control where logs are sent using `HttpRequestLogMode`.

```csharp
builder.AddQuilt4NetLogging()
    .AddHttpRequestLogging(o =>
    {
        o.LogHttpRequest = HttpRequestLogMode.ApplicationInsights | HttpRequestLogMode.Logger;
    });
```

| Value | Description |
|-------|-------------|
| `None` | No logging. |
| `ApplicationInsights` | Append request/response data to the request row (`customDimensions` on `AppRequests`). |
| `Logger` | Log via the standard `ILogger` pipeline (`AppTraces`). |

Values can be combined with `|` to log to multiple destinations.

`ApplicationInsights` writes the data as tags on the ASP.NET Core request span, which Application Insights SDK 3.x (`AddApplicationInsightsTelemetry`) and the Azure Monitor OpenTelemetry exporter both export into `customDimensions`. It also still fills `RequestTelemetry` for hosts on the 2.x SDK. With nothing recording requests, there is no request row and nothing is written. You do not need `builder.Logging.AddApplicationInsights()`; that method only existed in the 2.x logging package and only affected traces.

## Path filtering

By default, only paths matching `^/Api` (case-insensitive) are logged. Override with regex patterns.

```csharp
builder.AddQuilt4NetLogging()
    .AddHttpRequestLogging(o =>
    {
        o.IncludePaths = [".*"]; // Log all paths
    });
```

## Per-endpoint control

Use `[Logging]` and `[LoggingStream]` attributes to override logging behavior on individual endpoints.

```csharp
[Logging(RequestBody = true, ResponseBody = false)]
public async Task<IActionResult> StreamData() { ... }

[Logging(Enabled = false)]
public IActionResult InternalEndpoint() { ... }

[LoggingStream] // Shorthand for ResponseBody = false
public async Task<IActionResult> StreamEvents() { ... }
```

The `[Logging]` attribute can be applied to methods or classes.

| Property | Default | Description |
|----------|---------|-------------|
| `Enabled` | `true` | Enable or disable all logging for the endpoint. |
| `RequestBody` | `true` | Log the request body. |
| `ResponseBody` | `true` | Log the response body. Set to `false` for streaming endpoints. |

## Interceptor

The `Interceptor` modifies or filters the captured request/response before it is logged — the single
hook for removing secrets (headers, body, …).

**By default it masks sensitive header values.** `Interceptor` is pre-set to
`MaskSensitiveHeadersInterceptor`, which replaces the values of `SensitiveHeaders` with `***` on both
request and response (case-insensitive name match), keeping the header key so its presence stays
visible without leaking the value. Default masked headers: `Authorization`, `X-API-KEY`,
`Proxy-Authorization`, `Cookie`, `Set-Cookie`.

Configure the masked set, log everything verbatim, or take full control:

```csharp
builder.AddQuilt4NetLogging()
    .AddHttpRequestLogging(o =>
    {
        // (a) tweak which headers the default interceptor masks:
        o.SensitiveHeaders = ["Authorization", "X-API-KEY", "X-My-Secret"];

        // (b) OR disable filtering entirely — log request/response verbatim:
        o.Interceptor = null;

        // (c) OR supply your own (optionally composing the built-in masking):
        o.Interceptor = async (request, response, properties, serviceProvider) =>
        {
            request.Headers.Remove("X-Internal-Token");
            return await o.MaskSensitiveHeadersInterceptor(request, response, properties, serviceProvider);
        };
    });
```

## Configuration

All options can be set via code or `appsettings.json`. Code takes priority over `appsettings.json`, which takes priority over defaults.

### Code configuration

```csharp
builder.AddQuilt4NetLogging()
    .AddHttpRequestLogging(o =>
    {
        o.LogHttpRequest = HttpRequestLogMode.ApplicationInsights;
        o.UseCorrelationId = true;
        o.MaxBodySize = 5_000_000;
        o.IncludePaths = ["^/Api", "^/webhook"];
        o.LogRequestBodyByDefault = true;
        o.LogResponseBodyByDefault = false;
    });
```

### appsettings.json

```json
{
  "Quilt4Net": {
    "ApiLogging": {
      "LogHttpRequest": 1,
      "UseCorrelationId": true,
      "MonitorName": "Quilt4Net",
      "MaxBodySize": 1000000,
      "IncludePaths": ["^/Api"],
      "LogRequestBodyByDefault": true,
      "LogResponseBodyByDefault": false,
      "SensitiveHeaders": ["Authorization", "X-API-KEY", "Proxy-Authorization", "Cookie", "Set-Cookie"]
    }
  }
}
```

Configuration path: `Quilt4Net:ApiLogging`

### LoggingOptions

| Property | Default | Description |
|----------|---------|-------------|
| `LogHttpRequest` | `ApplicationInsights` | Logging destination. Combine with `\|` for multiple. |
| `UseCorrelationId` | `true` | Enable `X-Correlation-ID` header tracking. |
| `MonitorName` | `"Quilt4Net"` | Monitor name for tracking log items. Set to empty to omit. |
| `MaxBodySize` | `1 MB` | Maximum body size to log. Set to `0` to disable body logging. |
| `IncludePaths` | `["^/Api"]` | Regex patterns (case-insensitive) for paths to include. |
| `LogRequestBodyByDefault` | `true` | Log request body by default. Override per endpoint with `[Logging]`. |
| `LogResponseBodyByDefault` | `false` | Log response body by default. Override per endpoint with `[Logging]`. |
| `SensitiveHeaders` | `Authorization`, `X-API-KEY`, `Proxy-Authorization`, `Cookie`, `Set-Cookie` | Header names (case-insensitive) whose values the default interceptor masks. |
| `Interceptor` | masks `SensitiveHeaders` | Callback to modify/filter logged data. Defaults to header masking; set to `null` to log verbatim. |

## Logged data

### Request

| Field | Description |
|-------|-------------|
| `Method` | HTTP method (GET, POST, etc.). |
| `Path` | Request path. |
| `Headers` | Request headers (sensitive header values are masked by default — see Sensitive header masking). |
| `Query` | Query string parameters. |
| `Body` | Request body (respects `MaxBodySize` limit). |
| `ClientIp` | Client IP address. |

### Response

| Field | Description |
|-------|-------------|
| `StatusCode` | HTTP status code. |
| `Headers` | Response headers. |
| `Body` | Response body (respects `MaxBodySize` limit). |
