# Quilt4Net.Toolkit.Health
[![GitHub repo](https://img.shields.io/github/repo-size/Quilt4/Quilt4Net.Toolkit?style=flat&logo=github&logoColor=red&label=Repo)](https://github.com/Quilt4/Quilt4Net.Toolkit)

Add configurable health endpoints, heartbeat telemetry, and service monitoring to .NET Web Applications.

## Get started

Install the NuGet package [Quilt4Net.Toolkit.Health](https://www.nuget.org/packages/Quilt4Net.Toolkit.Health) and register the services in `Program.cs`.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddQuilt4NetHealth();

var app = builder.Build();

app.UseRouting();
app.UseQuilt4NetHealth();

app.Run();
```

`UseRouting()` must be called before `UseQuilt4NetHealth()`.

## Endpoints

Six endpoints are available by default. The base path is `~/api/Health` (configurable via `Pattern` and `ControllerName`).

| Endpoint | Path | Purpose |
|----------|------|---------|
| **Live** | `/api/Health/live` | Returns `Alive` if the process is running. Use for container orchestration (Kubernetes, Azure). |
| **Ready** | `/api/Health/ready` | Checks essential components. Returns `Ready`, `Degraded`, or `Unready` (503). |
| **Health** | `/api/Health/health` | Full system check including all components and dependencies. Returns `Healthy`, `Degraded`, or `Unhealthy` (503). |
| **Dependencies** | `/api/Health/dependencies` | Checks external dependencies one level deep (no circular checks). |
| **Metrics** | `/api/Health/metrics` | Returns system metrics: CPU, memory, storage, GPU, and uptime. GET only. |
| **Version** | `/api/Health/version` | Returns application metadata: version, environment, IP address, machine name. GET only. |

All endpoints support both GET (returns JSON body) and HEAD (returns status in header), except Metrics and Version which are GET only.

## Components

Components are services or resources that your application depends on. Add them to include in Health and Ready checks.

### Inline check

```csharp
builder.AddQuilt4NetHealth(o =>
{
    o.AddComponent(new Component
    {
        Name = "database",
        Essential = true,
        CheckAsync = async sp =>
        {
            // perform check
            return new CheckResult { Success = true, Message = "Connected" };
        }
    });
});
```

Set `Essential = true` for critical components (failure = `Unhealthy`/`Unready`).
Set `Essential = false` for non-critical components (failure = `Degraded`).

### Component service

For complex checks, implement `IComponentService` to separate setup from logic.

```csharp
builder.AddQuilt4NetHealth(o =>
{
    o.AddComponentService<MyComponentService>();
});
```

#### IComponentService interface

```csharp
public interface IComponentService
{
    IEnumerable<Component> GetComponents();
}
```

Each `Component` defines a named health check:

```csharp
public record Component
{
    /// <summary>Name of the component. Must be unique.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Essential components report Unhealthy/Unready (503) on failure.
    /// Non-essential components report Degraded. Default: true.
    /// </summary>
    public bool Essential { get; init; } = true;

    /// <summary>Async function that performs the check.</summary>
    public required Func<IServiceProvider, Task<CheckResult>> CheckAsync { get; init; }

    /// <summary>Optional. Used instead of CheckAsync; the token cancels on Timeout or when the request is aborted.</summary>
    public Func<IServiceProvider, CancellationToken, Task<CheckResult>> CheckWithCancellationAsync { get; init; }

    /// <summary>Optional. Reuse the last result for this long. Default: null (run on every probe).</summary>
    public TimeSpan? CacheDuration { get; init; }

    /// <summary>Optional. Answer from the last result and refresh it in the background. Needs CacheDuration.</summary>
    public bool RefreshInBackground { get; init; }

    /// <summary>Optional. Report the check as failed when it takes longer than this. Default: null (no limit).</summary>
    public TimeSpan? Timeout { get; init; }
}
```

The `CheckAsync` function receives `IServiceProvider` so you can resolve any DI service within the check.
Resolve services from it rather than capturing them, since a check can run outside the request (see
[Slow checks](#slow-checks-timeout-caching-and-background-refresh)).

`CheckResult` is returned from each check:

```csharp
public record CheckResult
{
    public required bool Success { get; init; }
    public string Message { get; init; }
}
```

#### Example: database and external API check

```csharp
public class MyComponentService : IComponentService
{
    public IEnumerable<Component> GetComponents()
    {
        yield return new Component
        {
            Name = "database",
            Essential = true,
            CheckAsync = async sp =>
            {
                var db = sp.GetRequiredService<IDbConnection>();
                try
                {
                    await db.OpenAsync();
                    return new CheckResult { Success = true, Message = "Connected" };
                }
                catch (Exception ex)
                {
                    return new CheckResult { Success = false, Message = ex.Message };
                }
            }
        };

        yield return new Component
        {
            Name = "payment-api",
            Essential = false,
            CheckAsync = async sp =>
            {
                var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("payments");
                var response = await client.GetAsync("/health");
                return new CheckResult
                {
                    Success = response.IsSuccessStatusCode,
                    Message = response.IsSuccessStatusCode ? "Reachable" : $"Status {response.StatusCode}"
                };
            }
        };
    }
}
```

In this example, the database is `Essential = true` — if it fails, the service reports `Unhealthy` (503). The payment API is `Essential = false` — if it fails, the service reports `Degraded` but remains available.

Multiple `IComponentService` implementations can be registered. All components from all services are included in health checks.

### Real-world patterns

A typical `ComponentService` checks infrastructure dependencies (databases, caches) and service connectivity (Quilt4Net). Here is a complete example combining all common patterns.

```csharp
public class ComponentService : IComponentService
{
    private readonly IConfiguration _configuration;
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly ICacheMonitor _cacheMonitor;
    private readonly IConnectionService _connectionService;

    public ComponentService(
        IConfiguration configuration,
        IMongoDbServiceFactory mongoDbServiceFactory,
        ICacheMonitor cacheMonitor,
        IConnectionService connectionService)
    {
        _configuration = configuration;
        _mongoDbServiceFactory = mongoDbServiceFactory;
        _cacheMonitor = cacheMonitor;
        _connectionService = connectionService;
    }

    public IEnumerable<Component> GetComponents()
    {
        // MongoDB connectivity — one check per connection string.
        // Essential: if the database is down, the service cannot function.
        foreach (var cs in _configuration.GetSection("ConnectionStrings").GetChildren())
        {
            yield return new Component
            {
                Name = $"Database.{cs.Key}",
                Essential = true,
                CheckAsync = async _ =>
                {
                    var service = _mongoDbServiceFactory.GetMongoDbService(
                        () => new DatabaseContext { ConfigurationName = cs.Key });
                    var info = await service.GetInfoAsync();
                    return new CheckResult
                    {
                        Success = info.CanConnect,
                        Message = $"{info.Message} {info.Firewall}".TrimEnd()
                    };
                }
            };
        }

        // Cache health — one check per cache provider (Memory, Redis, etc.).
        // Not essential: the service can function without cache, just slower.
        foreach (var healthType in _cacheMonitor.GetHealthTypes())
        {
            yield return new Component
            {
                Name = $"Cache.{healthType.Type}",
                Essential = false,
                CheckAsync = async _ =>
                {
                    var response = await healthType.GetHealthAsync();
                    return new CheckResult
                    {
                        Success = response.Success,
                        Message = response.Message
                    };
                }
            };
        }

        // Quilt4Net service connectivity — one check per service type.
        // Not essential: content and config can fall back to cached/default values.
        foreach (var service in Enum.GetValues<Service>())
        {
            yield return new Component
            {
                Name = $"Quilt4Net.{service}",
                Essential = false,
                CheckAsync = async _ =>
                {
                    var result = await _connectionService.CanConnectAsync(service);
                    return new CheckResult
                    {
                        Success = result.Success,
                        Message = result.Message
                    };
                }
            };
        }
    }
}
```

#### Essential vs non-essential guidelines

| Check type | Essential | Reason |
|-----------|-----------|--------|
| Primary database | `true` | Service cannot read or write data without it. |
| Cache (Memory, Redis) | `false` | Service degrades (slower) but still functions. |
| Quilt4Net Content/Config | `false` | Falls back to cached or default values. |
| External payment API | `false` | Other features still work; payment can retry. |
| Authentication provider | `true` | Users cannot log in without it. |

### Slow checks: timeout, caching and background refresh

Every component's check starts at the same moment, off the request thread, so one slow or blocking check
does not hold up the others. Components come back in the order their checks finish.

**`Timeout`** bounds how long a check may take. When it runs out the component is reported as failed
(`Degraded` or `Unhealthy`, per `Essential`) with the message *Check timed out after …*. The time counts from
before the check is called, so a check that blocks before its first `await` is cut off too. With
`CheckWithCancellationAsync` the token is also cancelled, so a check that honours it can stop and free its
connection; one that ignores it is left to finish on its own while the response goes out without it.

**`CacheDuration`** reuses a result for that long, so a busy endpoint runs each check at most once per
window, and concurrent probes share one run. When the cached result has expired, the next probe waits for a
new one.

**`RefreshInBackground`** stops even that wait. With it (and a `CacheDuration`), a probe always answers from the
component's last result at once, and a refresh starts in the background when that result is `CacheDuration`
old. A hosted service also runs the check once when the app starts and again whenever the result reaches
`CacheDuration`, so it stays recent on an endpoint that is called only now and then. Only a probe that arrives
before the very first result waits.

```csharp
o.AddComponent(new Component
{
    Name = "Mailbox",
    Essential = false,
    CacheDuration = TimeSpan.FromMinutes(5),
    RefreshInBackground = true,
    Timeout = TimeSpan.FromSeconds(30),
    CheckWithCancellationAsync = async (sp, ct) =>
    {
        var mail = sp.GetRequiredService<IMailClient>();
        await mail.SignInAsync(ct);
        return new CheckResult { Success = true };
    }
});
```

- **Use it for slow, non-essential dependencies**: mail servers, third-party registers, anything that can take
  seconds. On an essential component a stale `Healthy` could hide an outage for up to `CacheDuration` plus
  `Timeout`.
- **`Timeout` now bounds a check nobody waits on**, so a slow but working dependency can be given longer than a
  probe would tolerate. A result is never older than about `CacheDuration` plus `Timeout` while the app runs.
- **A refresh runs in its own service scope**, never the request's, because the request is over by then. The
  check gets that scope's `IServiceProvider`. Components from an `IComponentService` are resolved again in that
  scope, so services captured in their delegates are fresh too.
- **`checkedAt`** in a cached component's details is the UTC time its result was produced, so a stale result is
  visible: `"checkedAt": "2026-11-23T12:00:00.0000000Z"`.

#### IConnectionService

`IConnectionService` verifies connectivity to the Quilt4Net server. It is registered automatically when `AddQuilt4NetRemoteConfiguration()` is called.

```csharp
public interface IConnectionService
{
    Task<ConnectionResult> CanConnectAsync(Service service);
}
```

The `Service` enum has two values: `Content` and `Configuration`. Each maps to its own configured address and API key. Results are cached after the first successful check.

## Dependencies

Register external services that use Quilt4Net Health API. The dependency endpoint calls their health check.

```csharp
builder.AddQuilt4NetHealth(o =>
{
    o.AddDependency(new Dependency
    {
        Name = "payment-service",
        Essential = true,
        Uri = new Uri("https://payment.example.com/api/Health/")
    });
});
```

### Behaviour on a failing dependency

A dependency probe never throws. If a dependency returns a non-success status code (e.g. `429 Too Many Requests`), an error page, or is unreachable, it is reported as:

- **`Unhealthy`** when the dependency is `Essential = true`
- **`Degraded`** when the dependency is `Essential = false`

The HTTP status code (or error message) is included in the component's `Details`, and a single `Warning` is logged — never an `Error`. A rate-limited or temporarily-unavailable non-essential dependency therefore degrades the report instead of crashing the health check or flooding telemetry with exceptions.

### Throttling dependency probes

By default each dependency is probed at most once per `DependencyProbeCacheTime` (10 seconds). Repeated `/api/Health` requests within that window reuse the cached probe result, and concurrent requests are coalesced into a single probe — so frequent health checks don't hammer (and get rate-limited by) the dependency.

```csharp
builder.AddQuilt4NetHealth(o =>
{
    // Cache each dependency probe for 30s (default is 10s).
    o.DependencyProbeCacheTime = TimeSpan.FromSeconds(30);

    // Or disable caching to probe on every request:
    // o.DependencyProbeCacheTime = TimeSpan.Zero;
});
```

| Property | Default | Description |
|----------|---------|-------------|
| `DependencyProbeCacheTime` | 10 seconds | How long a dependency probe result is cached before the dependency is re-probed. Concurrent requests are coalesced into one probe. Set to `TimeSpan.Zero` (or negative) to probe on every request. |

## Heartbeat

The heartbeat feature sends periodic availability telemetry to Application Insights.

### Enable heartbeat

```csharp
builder.AddQuilt4NetHealth(o =>
{
    o.Heartbeat.Enabled = true;
    o.Heartbeat.Interval = TimeSpan.FromMinutes(5);
});
```

The heartbeat starts when `UseQuilt4NetHealth()` is called.

### TelemetryClient resolution

The heartbeat resolves a `TelemetryClient` in this order:

1. **From DI** - If `AddApplicationInsightsTelemetry()` has been called, that client is used.
2. **From HeartbeatOptions** - If `Heartbeat.ConnectionString` is set, a client is created from it.
3. **None** - A warning is logged and heartbeat execution is skipped.

```csharp
builder.AddQuilt4NetHealth(o =>
{
    o.Heartbeat.Enabled = true;
    o.Heartbeat.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
});
```

### HeartbeatOptions

| Property | Default | Description |
|----------|---------|-------------|
| `Enabled` | `false` | Enable/disable the heartbeat background service. |
| `Interval` | 5 minutes | Time between heartbeat executions. |
| `ConnectionString` | `null` | Application Insights connection string. Used only if no `TelemetryClient` is registered via DI. |

## Service probe

Monitor the health of background services and hosted services through a pulse mechanism.

Inject `IHostedServiceProbe<T>` into your hosted service and call `Pulse()` on each iteration.

```csharp
public class MyBackgroundService : BackgroundService
{
    private readonly IHostedServiceProbe _probe;

    public MyBackgroundService(IHostedServiceProbe<MyBackgroundService> probe)
    {
        _probe = probe.Register(plannedInterval: TimeSpan.FromSeconds(30));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _probe.Pulse();

            // do work...

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
```

The probe tracks pulse timing and reports status in Health responses:
- **Healthy** - Pulse received within the expected interval.
- **Degraded** - Pulse slightly delayed.
- **Unhealthy** - Pulse significantly delayed or missing.

## Certificate check

Automatically checks SSL certificate expiry for your service and its dependencies.

| Property | Default | Description |
|----------|---------|-------------|
| `Certificate.SelfCheckEnabled` | `true` | Check your own certificate. |
| `Certificate.DependencyCheckEnabled` | `true` | Check dependency certificates. |
| `Certificate.SelfCheckUri` | `null` | Explicit URI for self-check. Uses request host if not set. |
| `Certificate.CertExpiryDegradedLimitDays` | `30` | Days remaining before reporting `Degraded`. |
| `Certificate.CertExpiryUnhealthyLimitDays` | `3` | Days remaining before reporting `Unhealthy`. |

## Configuration

All options can be set via code, `appsettings.json`, or a combination of both. Priority order:

1. **Code** - Highest priority, overrides everything.
2. **appsettings.json** - Overrides defaults.
3. **Defaults** - Used when nothing else is configured.

### Code configuration

```csharp
builder.AddQuilt4NetHealth(o =>
{
    o.Pattern = "api";
    o.ControllerName = "Health";
    o.FailReadyWhenDegraded = true;
    o.Heartbeat.Enabled = true;
});
```

### appsettings.json

```json
{
  "Quilt4Net": {
    "HealthApi": {
      "Pattern": "api",
      "ControllerName": "Health",
      "FailReadyWhenDegraded": true,
      "Heartbeat": {
        "Enabled": true,
        "Interval": "00:05:00",
        "ConnectionString": "InstrumentationKey=..."
      },
      "Certificate": {
        "CertExpiryDegradedLimitDays": 30,
        "CertExpiryUnhealthyLimitDays": 3
      }
    }
  }
}
```

### Quilt4NetHealthApiOptions

| Property | Default | Description |
|----------|---------|-------------|
| `Pattern` | `"api"` | URL segment between base address and controller name. |
| `ControllerName` | `"Health"` | Controller name in the URL path. |
| `DefaultAction` | `"Health"` | Default endpoint when no action is specified. |
| `FailReadyWhenDegraded` | `false` | Return 503 from Ready when the system is degraded. |
| `OverrideState` | `null` | Override state (`Visible`, `Hidden`, `Disabled`) for all endpoints. |
| `AuthScheme` | `"ApiKeyScheme"` | Authentication scheme used for endpoint access. |
| `ExceptionDetail` | Environment-based | Level of exception detail: `Hidden`, `Message`, or `StackTrace`. |
| `IpAddressCheckUri` | `http://ipv4.icanhazip.com/` | URI for IP address lookup. Set to `null` to disable. |

### Endpoint access

Each endpoint has configurable access control and visibility.

```csharp
builder.AddQuilt4NetHealth(o =>
{
    o.Endpoints[HealthEndpoint.Metrics].Get.Access.Level = AccessLevel.AuthenticatedOnly;
    o.Endpoints[HealthEndpoint.Health].Get.State = EndpointState.Hidden;
});
```

**EndpointState**: `Visible` (shown in docs), `Hidden` (accessible but not in docs), `Disabled` (not accessible).

**AccessLevel**: `Everyone`, `AuthenticatedOnly`.

**DetailsLevel**: `Everyone`, `AuthenticatedOnly`, `NoOne` - controls response detail in GET requests.

> **Note:** The Version endpoint always returns `Version`, `Environment`, and `Is64BitProcess` regardless of `DetailsLevel` or authentication status. Only `Machine` and `IpAddress` are considered sensitive and are hidden for unauthenticated users (when `AuthenticatedOnly`) or for everyone (when `NoOne`).

### Environment defaults

| Setting | Development | Production | Other |
|---------|------------|------------|-------|
| Endpoint state | Visible | Hidden | Visible |
| Access level | Everyone | AuthenticatedOnly | Everyone |
| Details level | Everyone | AuthenticatedOnly | AuthenticatedOnly |
| Exception detail | StackTrace | Hidden | Message |

## Troubleshooting

**Startup error:** `EndpointRoutingMiddleware matches endpoints setup by EndpointMiddleware...`

Add `app.UseRouting()` before `app.UseQuilt4NetHealth()` in `Program.cs`.
