# Quilt4Net Toolkit
[![GitHub repo Issues](https://img.shields.io/github/issues/Quilt4/Quilt4Net.Toolkit?style=flat&logo=github&logoColor=red&label=Issues)](https://github.com/Quilt4/Quilt4Net.Toolkit/issues?q=is%3Aopen)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

NuGet packages that connect a .NET application to [Quilt4Net](https://quilt4net.com),
a service that runs the parts every application needs once it is live:

- **Remote configuration and feature toggles**, read while the application runs.
- **Content** in every language, with AI translation.
- **Health endpoints, heartbeats and uptime**, with alerts to your phone.
- **Logging** to Application Insights, tagged with application, environment and version.
- **Value groups** that hand settings and secrets to the application at start.

Each package also works on its own: the health endpoints and the request logging
need no Quilt4Net account. With an account and an API key, the same packages read
their settings and content from Quilt4Net and report back to it.
[What Quilt4Net does](https://quilt4net.com/product).

## Packages

### Quilt4Net.Toolkit
[![NuGet](https://img.shields.io/nuget/v/Quilt4Net.Toolkit)](https://www.nuget.org/packages/Quilt4Net.Toolkit)
![Nuget](https://img.shields.io/nuget/dt/Quilt4Net.Toolkit)

Core library providing feature toggles, remote configuration, content management, health check client, Application Insights client, universal telemetry tagging (`AddQuilt4NetLogging`), and measure/count logging extensions. Used by all other packages in this repository.

[Read more](Quilt4Net.Toolkit/README.md) | [Console sample](Quilt4Net.Toolkit.Console.Sample/)

### Quilt4Net.Toolkit.Health
[![NuGet](https://img.shields.io/nuget/v/Quilt4Net.Toolkit.Health)](https://www.nuget.org/packages/Quilt4Net.Toolkit.Health)
![Nuget](https://img.shields.io/nuget/dt/Quilt4Net.Toolkit.Health)

Health endpoints (live, ready, health, dependencies, metrics, version), heartbeat telemetry, service probe monitoring, and certificate checks for .NET Web Applications. Depends on `Quilt4Net.Toolkit`.

[Read more](Quilt4Net.Toolkit.Health/README.md) | [API sample](Quilt4Net.Toolkit.Api.Sample/)

### Quilt4Net.Toolkit.Api
[![NuGet](https://img.shields.io/nuget/v/Quilt4Net.Toolkit.Api)](https://www.nuget.org/packages/Quilt4Net.Toolkit.Api)
![Nuget](https://img.shields.io/nuget/dt/Quilt4Net.Toolkit.Api)

HTTP request/response logging and correlation ID tracking middleware. Logs to Application Insights and/or standard `ILogger` with path filtering and per-endpoint control via attributes. Depends on `Quilt4Net.Toolkit`.

[Read more](Quilt4Net.Toolkit.Api/README.md) | [API sample](Quilt4Net.Toolkit.Api.Sample/)

### Quilt4Net.Toolkit.Blazor
[![NuGet](https://img.shields.io/nuget/v/Quilt4Net.Toolkit.Blazor)](https://www.nuget.org/packages/Quilt4Net.Toolkit.Blazor)
![Nuget](https://img.shields.io/nuget/dt/Quilt4Net.Toolkit.Blazor)

Blazor component library for content management, language selection, page titles, role-based content admin, remote configuration admin, and Application Insights log viewing. Built on Radzen. Depends on `Quilt4Net.Toolkit`.

[Read more](Quilt4Net.Toolkit.Blazor/README.md) | [Server sample](Quilt4Net.Toolkit.Blazor.Server.Sample/) | [WASM sample](Quilt4Net.Toolkit.Blazor.Wasm.Sample/)

## Package dependencies

```
Quilt4Net.Toolkit              (core, no dependencies on other packages)
├── Quilt4Net.Toolkit.Health   (health endpoints, heartbeat, service probe)
├── Quilt4Net.Toolkit.Api      (API logging middleware)
└── Quilt4Net.Toolkit.Blazor   (Blazor components)
```

## Quick start

Install the packages you need and register the services in `Program.cs`.

```csharp
var builder = WebApplication.CreateBuilder(args);

// Health endpoints
builder.AddQuilt4NetHealth();

// Telemetry tagging + HTTP request logging
builder.AddQuilt4NetLogging()
    .AddHttpRequestLogging();

// Feature toggles and remote configuration
builder.AddQuilt4NetRemoteConfiguration();

// Content management (Blazor components)
builder.AddQuilt4NetBlazorContent();

var app = builder.Build();

app.UseRouting();
app.UseQuilt4NetHealth();
app.UseQuilt4NetLogging();

app.Run();
```

Add your API key in `appsettings.json`.

```json
{
  "Quilt4Net": {
    "ApiKey": "YOUR_API_KEY_HERE"
  }
}
```

## Configuration reference

All packages read from `appsettings.json` under the `Quilt4Net` section. The shared `ApiKey` is used by content, remote configuration, and feature toggles.

| Config path | Package | Description |
|-------------|---------|-------------|
| `Quilt4Net:ApiKey` | All | Shared API key from [Quilt4Net Web](https://quilt4net.com). |
| `Quilt4Net:HealthApi` | Health | Health endpoint options (pattern, controller name, heartbeat, certificate). |
| `Quilt4Net:ApiLogging` | Api | API logging options (log mode, paths, body size, correlation ID). |
| `Quilt4Net:RemoteConfiguration` | Toolkit | Remote configuration and feature toggle options (TTL). |
| `Quilt4Net:Content` | Toolkit / Blazor | Content management options (application name, server address, `WarmUpEnabled` bulk cache warm-up and its periodic re-warm, failure back-off). |
| `Quilt4Net:ApplicationInsights` | Toolkit | Application Insights client options (tenant, workspace, credentials). |
| `Quilt4Net:HealthClient` | Toolkit | Health client options (remote health API address). |
| `Quilt4Net:HeartbeatReporting` | Toolkit | Heartbeat reporting options (capabilities, interval, API key with `heartbeat:write`). |
| `Quilt4Net:Notification` | Toolkit | Notification client options (server address, API key with `notification:write`, application, environment). |

See individual package READMEs for full option details.

## Samples

Four sample projects demonstrate different platforms and features.

| Sample | Type | Features |
|--------|------|----------|
| [Quilt4Net.Toolkit.Api.Sample](Quilt4Net.Toolkit.Api.Sample/) | ASP.NET Core Web API | Health endpoints, API logging, authentication, feature toggles, remote configuration, measurements, Application Insights, hosted services |
| [Quilt4Net.Toolkit.Blazor.Server.Sample](Quilt4Net.Toolkit.Blazor.Server.Sample/) | Blazor Server | Content components, language selector, edit mode, remote configuration admin, log viewer with tab deep linking |
| [Quilt4Net.Toolkit.Blazor.Wasm.Sample](Quilt4Net.Toolkit.Blazor.Wasm.Sample/) | Blazor WebAssembly | Content components, language selector, edit mode, data grid with content-driven columns |
| [Quilt4Net.Toolkit.Console.Sample](Quilt4Net.Toolkit.Console.Sample/) | Console | Feature toggles, remote configuration, health client |
