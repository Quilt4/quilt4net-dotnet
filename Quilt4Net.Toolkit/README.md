# Quilt4Net.Toolkit
[![GitHub repo](https://img.shields.io/github/repo-size/Quilt4/Quilt4Net.Toolkit?style=flat&logo=github&logoColor=red&label=Repo)](https://github.com/Quilt4/Quilt4Net.Toolkit)

Core library providing health checks, metrics, remote configuration, feature toggles, content management, and Application Insights integration for .NET applications.

Features can be configured and monitored using [Quilt4Net Web](https://quilt4net.com).

## Feature toggles and remote configuration

Manage configuration and feature flags remotely via [Quilt4Net Web](https://quilt4net.com) or with a Blazor component from [Quilt4Net.Toolkit.Blazor](https://github.com/Quilt4/Quilt4Net.Toolkit/tree/master/Quilt4Net.Toolkit.Blazor).

Feature toggles are boolean values of remote configuration.

### Get started

Install the NuGet package [Quilt4Net.Toolkit](https://www.nuget.org/packages/Quilt4Net.Toolkit) and register the service.

```csharp
builder.AddQuilt4NetRemoteConfiguration();
```

Add your API key from [Quilt4Net Web](https://quilt4net.com) in `appsettings.json`.

```json
{
  "Quilt4Net": {
    "ApiKey": "YOUR_API_KEY_HERE"
  }
}
```

### Feature toggles

Inject `IFeatureToggleService` to check boolean feature flags.

```csharp
public class MyService
{
    private readonly IFeatureToggleService _featureToggle;

    public MyService(IFeatureToggleService featureToggle)
    {
        _featureToggle = featureToggle;
    }

    public async Task DoWorkAsync()
    {
        if (await _featureToggle.GetToggleAsync("new-feature", fallback: false))
        {
            // new feature logic
        }
    }
}
```

### Remote configuration

Inject `IRemoteConfigurationService` for typed configuration values.

```csharp
var maxRetries = await _configService.GetAsync("MaxRetries", fallback: 3);
```

### Application scoping (shared by default)

Toggles and configuration are **shared across applications by default** — a call with just a key reads
the team's shared value, so one flag controls every application that asks for it:

```csharp
// Shared (default): every application sees the same value.
await _featureToggle.GetToggleAsync("new-feature");
```

To read a value specific to **one application**, pass its name explicitly:

```csharp
await _featureToggle.GetToggleAsync("new-feature", application: "billing-service");
```

Passing `application: null` resolves to the configured `RemoteConfigurationOptions.Application`, falling
back to the entry assembly name — i.e. "this application".

> **Changed in 0.8.0:** the default scope is now **shared** (`application: ""`). Previously a bare call
> resolved to the current application. Pass `application: null` (or set `RemoteConfigurationOptions.Application`)
> for the previous per-application behavior.

### RemoteConfigurationOptions

| Property | Default | Description |
|----------|---------|-------------|
| `ApiKey` | `null` | API key from [Quilt4Net Web](https://quilt4net.com). |
| `Quilt4NetAddress` | `"https://quilt4net.com/"` | Quilt4Net server address. |
| `Application` | `null` | Application used when a read passes `application: null`. When `null`, the entry assembly name is used; set to `""` to read shared values. The read methods' own `application` parameter defaults to `""` (shared), so this option only takes effect when a call explicitly passes `null`. |
| `Ttl` | `null` | Client-requested time-to-live for cached values. When `null`, the team/server-configured default applies. |
| `HttpTimeout` | `5s` | Timeout for HTTP calls to the server. |
| `StaleWhileRevalidate` | `true` | When `true`, an expired value is returned immediately and refreshed in the background. Set `false` to refresh synchronously so callers always get a fresh value (subject to `HttpTimeout`). |
| `FailureCacheDuration` | `5s` | How long to stop calling for a key after a **failed** call. Doubles per consecutive failure up to `MaxFailureCacheDuration`, and resets on the first success. |
| `MaxFailureCacheDuration` | `5m` | Ceiling for that back-off, so a sustained outage settles into a low request rate instead of retrying every few seconds per key. |
| `MetricsEnabled` | `true` | Publish configuration resolutions on the `Quilt4Net.Toolkit.Configuration` meter (see *Metrics* below). |

Configuration path: `Quilt4Net:RemoteConfiguration`

### Metrics

Both clients publish resolution metrics, so a host gets **call volume, latency and cache-hit ratio**
without turning on `Debug` logging. Subscribe by meter name — nothing else is needed:

```csharp
builder.Services.AddOpenTelemetry().WithMetrics(m => m
    .AddMeter(Quilt4NetMetrics.ContentMeterName)
    .AddMeter(Quilt4NetMetrics.ConfigurationMeterName));
```

| Instrument | Kind | Tags |
|---|---|---|
| `quilt4net.content.resolutions` | counter | `source`, `application`, `language`, `stale` |
| `quilt4net.content.resolution.duration` | histogram (ms) | same |
| `quilt4net.content.backoff.keys` | gauge | — |
| `quilt4net.configuration.resolutions` | counter | `source`, **`key`**, `application`, `environment`, `stale` |
| `quilt4net.configuration.resolution.duration` | histogram (ms) | same |
| `quilt4net.configuration.backoff.keys` | gauge | — |

**The cache-hit ratio is the number these exist for**, and it cannot be computed outside the library:
the public reads return a bare value, so a wrapper can time a call and never learn whether it came
from cache, stale cache, the server or a fallback. Group the counter by `source` and the ratio falls
out. A key stuck on `Fallback` is the shape of a client pinned to its default by a fault.

**The content key is deliberately not a tag** — it is unbounded (over 1,200 in one reported
application) and would blow up cardinality; per-key volume stays in the `Debug` log, which already
carries it. The **configuration key is** a tag, because a host has a handful of toggles and "which
toggle is falling back" is the question actually asked.

The two gauges report how many keys are currently held off by the failure back-off, so a client
backing off is visible directly rather than inferred from gaps between attempts.

Cost is near zero when nobody subscribes; set `MetricsEnabled` to `false` on either options type to
opt out entirely.

#### Knowing whether a value is real

`GetToggleAsync("X", false)` returning `false` cannot be told apart from a server that says `false`,
so an application pinned to its fallback by a network fault looks exactly like one deliberately
switched off. `GetToggleResultAsync` returns the value together with its provenance:

```csharp
var result = await featureToggleService.GetToggleResultAsync("AssistantPanel.Enabled", fallback: false);

if (result.Source == ConfigurationSource.Fallback)
    logger.LogWarning("Assistant panel state is the local fallback, not the server's answer.");
```

| `ConfigurationSource` | Meaning |
|---|---|
| `Server` | Fetched from Quilt4Net.Server on this call. |
| `Cache` | Served from the local cache, within its lifetime. |
| `StaleCache` | Served past its lifetime while a background refresh runs. |
| `Fallback` | The caller's fallback — nothing was reached, or the server has no value for the key. |
| `Unknown` | The implementation does not report provenance (the default interface implementation). |

A failed call is held only for `FailureCacheDuration` (widening per consecutive failure), so a
recovered server is picked up in seconds rather than at the end of a full cache lifetime.

## Content management

Manage multilingual content from [Quilt4Net Web](https://quilt4net.com).

```csharp
builder.AddQuilt4NetContent();
```

Inject `IContentService` to retrieve and manage content.

```csharp
var (value, success) = await _contentService.GetContentAsync("welcome-message", "Hello!", languageKey, ContentFormat.String);
```

#### Resolving a whole view's keys at once

A view usually knows its keys up front. Resolving them one at a time works, but it is one `await`
per key — a dialog with 42 call sites plus a few option builders that loop over an enum ends up
making around seventy sequential calls to open once.

```csharp
var text = await _contentService.GetManyAsync(
[
    new ContentRequest { Key = "dialog.title",  DefaultValue = "Register case" },
    new ContentRequest { Key = "dialog.submit", DefaultValue = "Submit" },
    new ContentRequest { Key = "dialog.cancel", DefaultValue = "Cancel" },
]);

<h2>@text["dialog.title"]</h2>
```

The returned dictionary **always has an entry for every key you asked for** — the stored value where
there is one, your own `DefaultValue` where there is not — so there is no missing-key case to
handle.

What it actually costs:

- **Keys already cached**: nothing. No round trip at all.
- **A language nobody warmed** (one the user picked at runtime, not listed in `WarmUpLanguages`):
  one bulk warm for the whole language, rather than a call per key.
- **Keys the server has genuinely never seen**: still one call each — there is no bulk
  resolve-or-create — but they run together rather than in sequence, and they no longer block your
  render (see below).

The per-key `GetAsync` is unchanged and remains right for one-offs.

#### The first request for a new key does not block the render

The first time anyone asks for a key the server has never seen, the call is really a **write**: your
code supplies the text, the server stores it, and the caller waits for the storing. That was
measured at 1.5–3.3 seconds *per key* — enough to make a dialog that declares thirteen new keys take
about eighteen seconds to open the first time after a release, and 106 ms every time after.

Since the caller already holds the correct text, it no longer waits. `MaterializationWait`
(default 200 ms) bounds the wait; past it you get your own declared default, the key still
materializes in the background, and the next render shows the stored value.

> ⚠️ This applies to any key not in the cache, not only to new ones — the client cannot tell them
> apart until the server answers. For an *existing* key whose cache is cold, a slow server therefore
> means one render of the code default before the stored value arrives. In practice the warm-up has
> already loaded every existing key, so the cold case is close to exclusively new keys. Set
> `MaterializationWait` to `TimeSpan.Zero` if a correct first paint matters more than a responsive
> one.

#### Changing text a key already holds

A key's stored value wins over the default you supply — that is what keeps an administrator's edit
from being clobbered by a redeploy. The consequence is that **changing the literal in code does
nothing** once a key exists, and the workarounds are minting `Heading2` or hand-editing every
environment.

`IContentWriteService` is the supported way to change it:

```csharp
var result = await _contentWriteService.ImportAsync(
[
    new ContentImportItem
    {
        Key = "Dashboard.Feature.Add.Heading",
        Application = "MyApp",
        Instance = string.Empty,
        DefaultValue = "Activate {0}",
        Translations = new Dictionary<string, string> { ["Swedish"] = "Aktivera {0}" },
    },
]);

if (result.IgnoredLanguages.Count > 0) throw new InvalidOperationException(
    $"Unrecognised language name(s): {string.Join(", ", result.IgnoredLanguages)}");
```

- Needs an API key holding **`content:write`**. Writes land at the lowest stage; a human promotes.
- **A language you omit is left untouched**, so a Swedish fix cannot blank English. Omit unchanged
  values rather than resending them — a rewritten value is stamped as human-authored, and rewriting
  an untouched machine translation would make that output eligible as a translation source.
- **Always check `IgnoredLanguages`.** A name matching no configured language is skipped, not
  refused — `"Svenska"` where the server says `"Swedish"` otherwise returns success while the
  translation never appears.
- **`Application` and `Instance` are matched exactly.** Naming a coordinate the row was not stored
  under addresses a different row and creates a second one. If an import reports success and nothing
  changes, check this first.

> ⚠️ **Do not call this at startup.** Nothing calls it for you, and that is deliberate: every
> instance asserting its build's strings on boot recreates the problem it solves, and makes a
> rollback quietly revert tenant content. Call it from a reviewed maintenance action or a CI step.

#### Knowing where a value came from

`GetContentResultAsync` returns the same value plus its provenance, so you can tell a real server
value apart from a cache hit or a hard-coded fallback.

```csharp
var result = await _contentService.GetContentResultAsync("welcome-message", "Hello!", languageKey, ContentFormat.String);

if (result.Source == ContentSource.Default)
{
    // The server has no value for this key — the caller's default is being rendered.
}
```

| `ContentSource` | Meaning |
|---|---|
| `Server` | Fetched from Quilt4Net.Server on this call. |
| `Cache` | Served from the local cache, within TTL, from a value the server supplied. |
| `StaleCache` | Served from the local cache past its TTL; a background refresh was started. |
| `Default` | The caller's default was used — no override on the server (404), unreachable, or timed out. Also reported for a cached *default*, so a negative-cache hit is never mistaken for a real cache hit. |
| `Developer` | Developer language is active; every key resolves to a placeholder. |
| `NoApiKey` | No API key configured, so no lookup was attempted. |
| `Unknown` | The `IContentService` implementation does not report provenance (custom or test implementations that only override `GetContentAsync`). |

> `Success` on the legacy tuple is **not** a source discriminator — it is `true` for a cache hit, a
> stale hit and a fresh fetch alike, and `false` for a missing API key, a 404, a timeout and an
> error alike. Use `Source` when the distinction matters.

### ContentOptions

| Property | Default | Description |
|----------|---------|-------------|
| `Application` | Assembly name | Application name. |
| `Quilt4NetAddress` | `"https://quilt4net.com/"` | Quilt4Net server address. |
| `ApiKey` | `null` | API key from [Quilt4Net Web](https://quilt4net.com). |
| `StaleWhileRevalidate` | `true` | When `true`, an expired value is returned immediately and refreshed in the background. Set `false` to refresh synchronously so callers always get a fresh value (subject to `HttpTimeout`). |
| `SlowLogThreshold` | `3s` | When a content fetch or language-list load from the server takes at least this long, a single `Warning` is logged (endpoint, elapsed, HTTP status) — so slow loads surface even with `Debug` off. Set `TimeSpan.Zero` to disable. |
| `MaterializationWait` | `200ms` | How long a caller waits for a key not in the cache before rendering its own declared default. The key still materializes in the background. `TimeSpan.Zero` restores the previous behaviour of waiting up to `HttpTimeout`. |
| `BatchWindow` | `20ms` | Keys not in the cache that are asked for within this window go to the server as one batch request instead of one request each. A key alone in its window is sent as a normal single request, and a server without the batch endpoint is asked key by key. The window is spent out of `MaterializationWait`, so keep it short. `TimeSpan.Zero` turns batching off. |
| `BatchMaxSize` | `100` | The most keys in one batch request; a full batch is sent without waiting for the window. |
| `WarmUpEnabled` | `true` | Pre-fill the cache at startup with one bulk call per language (Blazor). Set `false` for lazy per-key loading only. |
| `WarmUpLanguages` | `[]` | Extra languages to warm at startup and on "Reload Content", by **name** (e.g. `["English", "Svenska"]`), on top of the always-warmed default. Empty = only the default warms at startup; others warm per-circuit on first selection. |
| `PeriodicWarmUpEnabled` | `true` | Repeat the bulk warm-up on a timer instead of once per process, so entries are replaced shortly **before** they expire and the per-key path is never reached in steady state. |
| `WarmUpRefreshFraction` | `0.8` | Where in the server's observed content lifetime the re-warm runs. `0.8` of a 10-minute lifetime re-warms every 8 minutes. |
| `MinimumWarmUpInterval` | `30s` | Floor for that interval, so a very short server lifetime cannot turn the re-warm into its own source of load. |
| `FailureCacheDuration` | `5s` | How long to stop calling for a key after a **failed** call (timeout, transport error, non-404 error status). Doubles per consecutive failure up to `MaxFailureCacheDuration`, and resets on the first success. |
| `MaxFailureCacheDuration` | `5m` | Ceiling for that back-off. |
| `CacheDuration` | `null` | Cache lifetime to **request** from the server. `null` uses the server's own. The server clamps a value above its maximum rather than refusing it. |
| `MetricsEnabled` | `true` | Publish content resolutions on the `Quilt4Net.Toolkit.Content` meter (see *Metrics* below). |
| `NotFoundCacheDuration` | `10m` | How long to remember a `404` — the server was reached and has no override for the key. Deliberately far longer than the failure back-off: a 404 is an answer, and re-asking every few seconds would re-request every unseeded key on nearly every render. |

Configuration path: `Quilt4Net:Content`

#### Request volume under load

Three behaviours decide how much traffic a content-heavy application generates, and they are meant
to be read together:

- **The bulk warm-up replaces the per-key fan-out.** Every warmed key shares one expiry, so without
  a repeat the whole set goes stale at the same instant and the next render issues one HTTP call per
  key. `PeriodicWarmUpEnabled` re-warms before that happens, and covers every language actually in
  use — configured ones plus any a user selected at runtime.
- **A failed call is held briefly, not for a cache lifetime.** Before, a failure was re-stamped with
  a full TTL, so every expiry landed on one failed call that bought another whole lifetime of the
  fallback value; there was no state in which a key converged while calls kept failing.
- **A `429` is honoured exactly, on every path.** `Retry-After` overrides the local back-off in both
  directions — the server saying when it will be ready beats any client-side guess. This now covers
  content, **remote configuration** and the **bulk warm-up**; a shed call is logged at `Warning` as
  backpressure rather than at `Error` as a fault.
- **A `403` serves the fallback and says why.** The key lacks the read scope (`config:read`,
  `content:read`, `language:read`), or the team has switched that feature - Remote configuration or
  Content - off on the server. Every read then serves its default, which is what switching it off
  means, and the refusal is logged at `Warning` with the server's own sentence, not at `Error`. For
  content it is logged at most every five minutes, since a page asks for many keys at once.
- **A rate-limited warm-up waits rather than falling back.** Dropping to per-key fetching would turn
  one shed call into hundreds — the exact burst the server shed it to avoid — so a `429` carrying a
  short `Retry-After` (up to 30 s) is waited out and retried once. A longer one is left to the next
  periodic re-warm rather than parked on a background task.
- **`CacheDuration` sets how often the re-warm runs at all.** The re-warm fires at
  `WarmUpRefreshFraction` of the lifetime the **server** reports, so asking for a longer one retunes it
  automatically: 24 hours at the default 0.8 is one bulk call per language every 19.2 hours instead of
  every 8 minutes. Nothing else needs changing. The trade-off is that a content edit takes up to that
  long to reach a running instance — "Reload Content" in the admin UI remains the immediate path.

#### Diagnostic logging

Content logging is split by whether a condition is **actionable**. These fire without any log
configuration:

| Condition | Level | Frequency |
|---|---|---|
| A key has no value on the server (404) | `Information` | Once per key per `FailureCacheDuration` — the negative cache stops it repeating per render. |
| No `ApiKey` configured, so content can never load | `Warning` | Once per process. |
| Server round-trip slower than `SlowLogThreshold` | `Warning` | Per slow call. |
| Timeout, or a non-404 failure response | `Warning` / `Error` | Per occurrence. |

A normal server fetch stays at `Debug` — it is the designed happy path, and at cold start it is one
line per key.

To diagnose slow or looping content/language loads, enable `Debug` logging for the content categories — no code change required:

```json
{
  "Logging": {
    "LogLevel": {
      "Quilt4Net.Toolkit.Features.Content.RemoteContentCallService": "Debug",
      "Quilt4Net.Toolkit.Blazor.LanguageStateService": "Debug"
    }
  }
}
```

At `Debug` you get, per content read, the key, resolved language + application, the resolved
`ContentSource` and elapsed time; per language load, the source (cache vs server), count and elapsed
time; and, in Blazor, each language reload (with timing) and every selected-language change that
triggers a content reload. Genuinely slow server round-trips are logged at `Warning` regardless (see `SlowLogThreshold`).

## Release notes

Publish one markdown note per application version, and let an application ask what has changed
since the version it last showed a user — "what's new since you were last here".

The toolkit is the **reading** half. Notes are **written** over plain REST or MCP from a release
pipeline, which needs no NuGet dependency. Registration comes with content, since it is the same
server, the same api key and the same application name:

```csharp
builder.AddQuilt4NetContent();
```

```csharp
var news = await _releaseNoteService.GetDeltaAsync(lastSeenVersion);
if (news.HasNews)
{
    Render(news.Markdown);          // every note, newest first, already concatenated
    Remember(news.LatestVersion);   // the mark to pass in next time
}
```

Three things about it are deliberate:

**You own the "last seen" mark.** The server keeps no per-visitor state, and neither does the
toolkit. Store `LatestVersion` wherever you already keep per-user settings, and pass it back next
time.

**No version, no call.** A first-ever visitor has nothing to be caught up on, so do not call. Asking
with nothing is not a way to fetch the whole history — the call short-circuits in the client and
never reaches the server. Asking with something that is not SemVer (a `v1.2.3` tag, a four-part
`1.2.3.4` assembly version) does the same thing and logs a warning, because an empty delta otherwise
looks exactly like "you are up to date".

**Versions are strict SemVer 2.0.** Ordering is the whole feature and text ordering gets it wrong —
`1.10.0` sorts before `1.9.0` as a string. `SemanticVersion` is public if you want the same parsing
and comparison the server uses.

| Member | Description |
|--------|-------------|
| `GetDeltaAsync(sinceVersion, includePreRelease, application)` | Everything newer than `sinceVersion`, newest first. Never null: nothing new, nothing to ask from, and a server that did not answer all come back empty. Pre-releases are left out unless asked for. |
| `GetAsync(version, application)` | One version's note, or `null` when it has none. |

Release notes are decoration, so every failure degrades quietly — a page that cannot show its news
still renders. A **403** is logged as a warning with the server's own sentence: either the key lacks
`release:read`, or the team has switched its **Documentation** feature off, which is a choice rather
than a fault. Any other failure is logged as an error.

### Minting a key

In the Quilt4Net.Server admin UI under **Api → Api Key**. `release:read` comes with any key at Viewer
level or above, so a consuming application's existing key can already read the news. **`release:write`
is registered at Administrator**, so a key only carries it at that access level or as an explicit
scope override — publishing is a release-pipeline action, and every application key holds `User` for
content write-back, which would otherwise let any app embedding the toolkit announce a version. For
publishing over MCP the key also needs `mcp:discover`.

### Publishing a note

Not through this package — a release step should not need a NuGet reference to write one document:

```bash
curl -X POST https://quilt4net.com/Api/ReleaseNote \
  -H "X-API-KEY: $QUILT4NET_KEY" -H "Content-Type: application/json" \
  -d '{"application":"MyApp","version":"1.4.0","markdown":"## 1.4.0\n- Faster search"}'
```

Or the MCP tool `quilt4net.release_note.publish`, for a release step driven by an agent. Publishing a
version again replaces its body, so a rerun or a fixed typo leaves one note rather than two, and a
version that is not strict SemVer is refused there and then — ordering is what the delta is built on.

## Notifications

Send a notification to your team on Quilt4Net, where subscribers such as the Quilt4Net app pick it up.
A notification has a **category** and a **message**. The receiver decides from the category how to
alert — an alarm for `alarm`, a quiet notification for `info` — so agree on the categories in your team.

```csharp
builder.AddQuilt4NetNotifications();
```

```csharp
public class Worker(INotificationClient notifications)
{
    public Task ReportAsync() => notifications.SendAsync("alarm", "The import queue has stopped moving.");
}
```

The application, environment and machine name are sent along. Set them yourself on a
`SendNotificationRequest`, or in the options; otherwise the entry assembly's name, the host
environment's name and `Environment.MachineName` are used.

A failed send throws `NotificationClientException` with the status code. It is never dropped quietly:
a sender that believes it raised an alarm when it did not is the worst outcome. The answer's `Reached`
says how many phones were woken; zero means nobody will hear it.

`GetCategoriesAsync()` lists the team's notification categories, to pick from. It returns null from a
server that cannot list them (or a key without `notification:read`); any category can still be sent, and
one the team does not have is added.

### NotificationOptions

Bound from `Quilt4Net:Notification`, falling back to `Quilt4Net:ApiKey` and `Quilt4Net:Quilt4NetAddress`.

| Option | Default | Description |
|--------|---------|-------------|
| `Quilt4NetAddress` | `https://quilt4net.com/` | Address of the Quilt4Net server. |
| `ApiKey` | | Team API key. Needs the `notification:write` scope, which is chosen when the key is minted; ordinary application keys do not have it. |
| `Application` | entry assembly name | Application sent with each notification. |
| `Environment` | host environment name | Environment sent with each notification. |

## Heartbeat reporting

Tell Quilt4Net that this instance is alive and what it can do. A rule on Quilt4Net names a
**capability** and raises a notification when no instance with it has reported in time — and another
when it comes back. Run several machines with different capabilities, and you are alerted when the last
one that can, say, `import` goes quiet.

```csharp
builder.AddQuilt4NetHeartbeatReporting(o => o.Capabilities = ["import", "export"]);
```

A report is sent at start and then every `Interval`. Besides the application, environment, instance and
version, each tells Quilt4Net the machine name, runtime, operating system, when the process started, its
interval, the Toolkit version and the processor count. Quilt4Net shows each instance with an uptime bar
like an HTTP monitor's: a heartbeat more than two intervals and a minute late counts as a silence. A failed report is logged and retried on the next
interval, never thrown — the application keeps running when Quilt4Net cannot be reached, and the missed
heartbeat is what the rule is there to notice.

This is not the Application Insights heartbeat in `Quilt4Net.Toolkit.Health` (`Quilt4Net:HealthApi:Heartbeat`),
which sends availability telemetry.

### Metrics

Each heartbeat also carries **metrics**, which Quilt4Net keeps for 100 days and graphs per instance, the
way it graphs an HTTP monitor's response times. The heartbeat's `Interval` is their frequency too.

Built in, in percent:

| Metric | Option | Default | |
|--------|--------|---------|---|
| `cpu.process` | `Cpu` has `Process` | on | This application's processor use over all cores, since the previous heartbeat. Same on every platform. |
| `cpu.machine` | `Cpu` has `Machine` | off | The whole machine's, on Windows and Linux, from the second heartbeat on. Inside a container it is the host's. |
| `memory.process` | `Memory` has `Process` | on | This application's working set over the memory available to it. |
| `memory.machine` | `Memory` has `Machine` | off | All memory in use. Memory is measured against what is available to the process: the machine's, or the container's limit. |

`Cpu` and `Memory` are flags enums: `None`, `Process`, `Machine` or `Both`.

Add values of your own; each is read on every heartbeat:

```csharp
builder.AddQuilt4NetHeartbeatReporting(o =>
{
    o.Cpu = CpuMetric.Both;
    o.Memory = MemoryMetric.Both;
    o.AddMetric("queue.length", () => queue.Count, "items");
    o.AddMetric("cache.hit-ratio", async ct => await cache.GetHitRatioAsync(ct), "percent");
});
```

For values that need other services, implement `IHeartbeatMetricProvider`. It is resolved from
dependency injection in a scope of its own at each heartbeat, so it can take anything in its
constructor, a database context included:

```csharp
public class OrderQueueMetrics(IOrderQueueClient queue) : IHeartbeatMetricProvider
{
    public async Task<IEnumerable<HeartbeatMetric>> GetMetricsAsync(CancellationToken cancellationToken)
    {
        var status = await queue.GetStatusAsync(cancellationToken);
        return [new("queue.length", status.Length, "items"), new("queue.oldest", status.OldestAge.TotalSeconds, "s")];
    }
}

builder.AddQuilt4NetHeartbeatReporting(o => o.AddMetricProvider<OrderQueueMetrics>());
```

Every provider and every `AddMetric` value is read at the same time, sharing `MetricTimeout` (10 seconds
by default); pass the token on to the calls you make. One that throws, is late, is not a finite number,
or repeats a name already sent is logged and left out of that heartbeat; the heartbeat itself is always
sent. Names are at most 64 characters, and a heartbeat carries at most 20 metrics.

`IHeartbeatStatus` says what reporting is doing: the report last sent, when, what the server answered
(and why it refused, such as Monitoring being switched off for the team), and when the next one goes.
`SendNowAsync()` sends one outside the interval. It is what the Heartbeat tab of the Blazor
`Quilt4NetPanel` shows.

### HeartbeatReportingOptions

Bound from `Quilt4Net:HeartbeatReporting`, falling back to `Quilt4Net:ApiKey` and `Quilt4Net:Quilt4NetAddress`.

| Option | Default | Description |
|--------|---------|-------------|
| `Enabled` | `true` | Set `false` to stop reporting on a machine or in an environment, such as a developer's laptop, without changing code. |
| `Quilt4NetAddress` | `https://quilt4net.com/` | Address of the Quilt4Net server. |
| `ApiKey` | | Team API key. Needs `heartbeat:write`, which an ordinary application key has. |
| `Capabilities` | none | What this instance can do. Matched without case. |
| `Interval` | 5 minutes | How often to report. Make a rule's silence window a few intervals long. |
| `Application` | entry assembly name | Application to report. |
| `Environment` | host environment name | Environment to report. |
| `Instance` | machine name | Tells two copies of the application apart. |
| `Cpu` | `Process` | Which processor use to report: `None`, `Process`, `Machine` or `Both`. |
| `Memory` | `Process` | Which memory use to report: `None`, `Process`, `Machine` or `Both`. |
| `MetricTimeout` | 10 seconds | How long the application's own metrics may take at each heartbeat, all at once. |

## Health check client

Client for consuming health endpoints from a remote service that uses Quilt4Net Health API.

```csharp
builder.AddQuilt4NetHealthClient(o =>
{
    o.HealthAddress = "https://my-service.example.com/api/Health/";
});
```

Inject `IHealthClient` to call remote health endpoints.

```csharp
var health = await _healthClient.GetHealthAsync(cancellationToken);
var metrics = await _healthClient.GetMetricsAsync(cancellationToken);
var version = await _healthClient.GetVersionAsync(cancellationToken);
```

If the remote endpoint replies with a non-success status code, these methods throw `HttpRequestException` (carrying the status code) rather than a JSON parse error — wrap calls in try/catch when the remote service may be unavailable.

### HealthClientOptions

| Property | Default | Description |
|----------|---------|-------------|
| `HealthAddress` | `null` | Address to the remote health API. |

Configuration path: `Quilt4Net:HealthClient`

## Application Insights client

Client for querying Application Insights data (logs, metrics, exceptions). The toolkit supports two mutually exclusive registration modes:

**Local mode** — credentials in the consumer's `appsettings.json`:
```csharp
builder.AddQuilt4NetApplicationInsightsClient();
```

**Remote mode** — credentials fetched from Quilt4Net.Server at runtime using an API key with the `logging:read` scope (formerly `monitor:read`; a 403 is logged as a warning and returns no configurations). Consumers no longer need to keep `TenantId` / `WorkspaceId` / `ClientId` / `ClientSecret` in their own configuration:
```csharp
builder.AddQuilt4NetApplicationInsightsClientRemote();
```
```json
{
  "Quilt4Net": {
    "RemoteConfiguration": {
      "Quilt4NetAddress": "https://quilt4net.com/",
      "ApiKey": "<logging:read API key>"
    }
  }
}
```
Configuration path: `Quilt4Net:RemoteConfiguration` (the API key is also accepted at the top-level `Quilt4Net:ApiKey`). Keep the key in user-secrets or environment variables, not in committed config.

The remote provider caches the configuration list per its configured TTL (`RemoteConfigurationOptions.Ttl`, or the server default when unset) with stale-while-revalidate, so transient server outages don't break the consuming page. When more than one workspace is configured on the server for the team, every workspace is reachable; the Blazor `LogView` renders an in-component **dropdown** (one workspace) and `VersionMatrixDisplay` a **multi-select radio bar** that merges the matrix across the selected workspaces — selecting none shows all (see [Quilt4Net.Toolkit.Blazor README](https://github.com/Quilt4/Quilt4Net.Toolkit/blob/master/Quilt4Net.Toolkit.Blazor/README.md)).

> **Local and remote are mutually exclusive.** Register one or the other. If both a `Quilt4Net:ApplicationInsights` block and a remote `Quilt4Net:RemoteConfiguration` API key are configured, the remote source wins and the local block is silently ignored. In a Blazor host, use `AddQuilt4NetBlazorApplicationInsightsClientRemote()` so the workspace selector is wired up.

### ApplicationInsightsOptions

| Property | Default | Description |
|----------|---------|-------------|
| `TenantId` | `null` | Azure AD tenant ID (found under "Tenant properties" in Azure portal). Only required when `AuthMode = ClientSecret`. |
| `WorkspaceId` | `null` | Application Insights workspace ID. |
| `ClientId` | `null` | For `ClientSecret`: app registration client ID with `Data.Read` permission on Application Insights API. For `ManagedIdentity`: empty for system-assigned MI, or the user-assigned MI's client ID. For `DefaultAzureCredential`: optional hint, used as the preferred user-assigned MI when MI lights up in the chain. |
| `ClientSecret` | `null` | Client secret for the app registration. Only required when `AuthMode = ClientSecret`. |
| `AuthMode` | `ClientSecret` | Authentication mode: `ClientSecret` (service principal), `ManagedIdentity` (Azure-hosted apps), or `DefaultAzureCredential` (chained — same config works locally via `az login` and in Azure via MI). |
| `EnvironmentOrder` | `["Development", "CI", "Staging", "Test", "Production"]` | Preferred environment ordering for the version matrix. Names not listed render after, alphabetically; rows with empty env render last as `(unknown)`. |
| `ApplicationAlias` | `[]` | Static `raw → logical` alias map for `VersionMatrixDisplay` consumers that don't pass a per-component `AliasFolder` delegate. Each entry groups one or more raw `cloud_RoleName` values under a single logical application name. |

Configuration path: `Quilt4Net:ApplicationInsights`

#### Managed Identity

When the app runs in Azure (App Service, Container Apps, VMs, …) you can skip the client secret entirely and authenticate with the hosting identity:

```json
{
  "Quilt4Net": {
    "ApplicationInsights": {
      "WorkspaceId": "your-workspace-id",
      "AuthMode": "ManagedIdentity"
    }
  }
}
```

Grant the App Service identity the **Log Analytics Reader** (or Monitoring Reader) role on the target workspace. Use a user-assigned MI by setting `ClientId` to the identity's client ID; leave it empty for system-assigned.

#### DefaultAzureCredential

Use `DefaultAzureCredential` to share a single configuration across local dev and Azure-hosted environments:

```json
{
  "Quilt4Net": {
    "ApplicationInsights": {
      "WorkspaceId": "your-workspace-id",
      "AuthMode": "DefaultAzureCredential"
    }
  }
}
```

The chained credential probes (in order): environment variables → workload identity → Managed Identity → Visual Studio / VS Code account → Azure CLI (`az login`) → Azure PowerShell. The first that succeeds is used.

Typical setup:

- **Local development**: developer runs `az login` once. The toolkit picks up that token and queries the workspace directly — no service principal secret to copy into user-secrets.
- **Azure**: the App Service identity is used (same effect as `ManagedIdentity`). Grant it Log Analytics Reader as above.

`TenantId` and `ClientId` are forwarded as hints (filter to a specific tenant; prefer a specific user-assigned MI) — both can be left empty.

> **Trade-off**: `DefaultAzureCredential` masks *which* underlying credential succeeded. If authentication fails, the error chain is less specific than the explicit modes. For diagnosis, switch to `ClientSecret` or `ManagedIdentity` to isolate the issue.

## Value Groups

A **Value Group** is a server-curated bundle of references to existing values across multiple stores (today: feature toggles and Application Insights configurations; KV pairs and Atlas credentials come in later features). An external agent uses one HTTP call with its own group-scoped API key to receive a typed bundle containing only the values the operator allowlisted.

Use this when an agent needs least-privilege access to a specific deployment's configuration without exposing the team-wide scope.

```csharp
builder.AddQuilt4NetValueGroupClient(o =>
{
    o.ApiKey = builder.Configuration["Quilt4Net:ValueGroup:ApiKey"];
});
```

Then inject and call:

```csharp
public class MyAgent(IValueGroupClient client)
{
    public async Task DoWorkAsync(CancellationToken ct)
    {
        var bundle = await client.GetAsync(ct);
        foreach (var toggle in bundle.FeatureToggles) { /* ... */ }
        foreach (var ai in bundle.ApplicationInsightsConfigurations) { /* ... */ }
    }
}
```

### ValueGroupClientOptions

| Property | Default | Description |
|----------|---------|-------------|
| `Quilt4NetAddress` | `https://quilt4net.com/` | Server base URL. |
| `ApiKey` | — *(required)* | The API key minted for this Value Group in the server's admin UI. Must carry the `valuegroup:read` scope. The key is tag-bound server-side to exactly one Value Group; the server resolves which group from the key, so the client never names a group id. |
| `Ttl` | `5 min` | Cache freshness window. Subsequent calls within the window serve the cached bundle. |
| `HttpTimeout` | `5 s` | HTTP timeout. On timeout the cached bundle is served if available. |

Configuration path: `Quilt4Net:ValueGroup` (or top-level `Quilt4Net:ApiKey` + `Quilt4Net:Quilt4NetAddress` for shared keys).

### Behaviour and contract

- **Stale-while-revalidate**: returning a fresh bundle is the default. Stale-cache fallback applies on transient HTTP errors *and* on timeout.
- **`ValueGroupAuthorizationException` on 401/403**: a revoked key or wrong-binding response *throws*, by design. Unlike `IRemoteConfigurationService` (which silently serves fallback values), Value Groups carry secret-bearing data, so the consumer must learn it has been revoked rather than continue using cached secrets.
- **One client = one group**: register multiple clients via keyed services if the consumer needs more than one group.

### Minting a key

In the Quilt4Net.Server admin UI under **Value Groups**: select the group → API Keys panel → **Mint new key**. The raw key is shown exactly once — save it immediately. The key carries only the `valuegroup:read` scope and is tag-bound on the server side to this one group; it cannot reach any other team data.

## Issue tracker

A per-team issue tracker, meant to be driven by an agent over REST or MCP rather than by hand. Each issue carries a title, body text, a **route** (the roadmap lane it belongs to), a Now/Next/Later band, a workflow state, an optional assignee, an optional S/M/L effort and an optional Critical/Important/Nice importance. Issues are linked to each other with typed dependencies, and carry **references** to where they came from.

```csharp
builder.AddQuilt4NetIssues(o =>
{
    o.ApiKey = builder.Configuration["Quilt4Net:Issue:ApiKey"];
});
```

Then inject and call:

```csharp
public class MyAgent(IIssueService issues)
{
    public async Task TriageAsync(CancellationToken ct)
    {
        var created = await issues.CreateAsync(new CreateIssueRequest
        {
            Title = "Bulk warm-up fans out on a shed call",
            Route = "read path",
            Band = RoadmapBand.Now,
            Effort = IssueEffort.M
        }, ct);

        await issues.AddLinkAsync(created.Number, new AddIssueLinkRequest
        {
            TargetNumber = 12,
            Kind = IssueLinkKind.Blocks,
            Reason = "the limiter has to ship before the client can honour it"
        }, ct);
    }
}
```

### Content and plan

An issue has two text fields, both markdown:

- **`Content`** is the short what and why: the part a reader needs to judge the issue from a list.
- **`Plan`** is how it will be delivered: the spec, the step checklist, decisions taken on the way, the session log. It is the long part, and it lives on the issue so no plan has to sit in a file on one machine.

```csharp
await issues.CreateAsync(new CreateIssueRequest
{
    Title = "Bulk warm-up fans out on a shed call",
    Content = "A shed call re-warms every key at once, which is the load the limiter exists to stop.",
    Plan = "## Steps\n- [ ] honour Retry-After in the warm-up\n- [ ] test the shed path"
}, ct);
```

`UpdateAsync` replaces every other field, but **not the plan**: a `null` `Plan` leaves the stored plan as it is, and an empty string clears it. The plan arrived after the update contract shipped, so a client built before it cannot send one; replacing would let every such client wipe plans without knowing the field exists.

### Importance and effort

`Importance` (`Critical` / `Important` / `Nice`) and `Effort` (`S` / `M` / `L`) are the backlog's own vocabulary, so a tracker issue and a backlog row can be compared without translating between them. Together they give the house ordering rule: **importance first, then effort ascending** — highest impact for least work.

Both are **optional**. An issue with no importance means nobody has graded it yet, which is worth seeing; defaulting it to `Nice` would assert a judgement no one made and hide what still needs triage. Ungraded issues sort after everything graded.

Importance is deliberately **not** drawn on the roadmap. A map restates effort only — quick wins are invisible without it — while status, priority and ownership are read from the source rather than copied onto the figure.

### The three link kinds

| Kind | Drawn | Means |
|------|-------|-------|
| `Blocks` | solid | The target genuinely cannot start until the source ships. A hard constraint, and rare. |
| `Cheapens` | dashed | The target is cheaper, safer or better-informed afterwards, but is not prevented from starting. Most real edges are these. |
| `Overlaps` | dotted | Both touch the same surface, so doing them independently causes rework. Not an ordering — a warning to pick one owner. |

**Every link requires a `Reason`, and the server rejects an empty one.** Most items people assume are ordered turn out to be merely related; making the reason mandatory is what keeps the graph from filling up with dependencies nobody can justify. `Blocks` links may not form a cycle.

### References — where an issue came from

An issue carries a collection of `IssueReferenceRequest` / `IssueReferenceResponse`, each naming a `Kind` (the source system), a `Value` (what it points at there), an optional `Url` and `Label`, and a `Role`.

```csharp
await issues.CreateAsync(new CreateIssueRequest
{
    Title = "Content resolution times out at 15s",
    References =
    [
        new IssueReferenceRequest
        {
            Kind = "github",
            Value = "Toolkit#172",
            Role = IssueReferenceRole.Identity,
            Url = "https://github.com/Quilt4/Quilt4Net.Toolkit/issues/172"
        },
        new IssueReferenceRequest { Kind = "code", Value = "ContentService.cs:195" }
    ]
}, ct);
```

**A collection, not a single field**, because one source is often wrong: an issue can be a backlog row *and* GitHub #161 at the same time, and an importer forced to pick one loses the other.

**`Kind` is an open string**, unlike `IssueLinkKind`, `RoadmapBand` and `IssueEffort`. Those are closed because the roadmap format defines them; source systems are an extensibility point, and an enum would force a Toolkit release every time a new tracker appeared. Kinds are lower-cased on write, so `GitHub` and `github` are one kind.

**`Url` is optional.** A code comment and a backlog row genuinely have none, so `Value` carries the meaning and the link is a bonus.

| Role | Means | Unique? |
|------|-------|---------|
| `Provenance` *(default)* | A hint about where the issue came from. Allowed to go stale and never load-bearing — `LanguageController.cs:52` is wrong after the next edit, and that is fine. | No. Many issues legitimately share one origin. |
| `Identity` | This issue **is** that ticket. A key, not a hint. | **Yes.** The server refuses a second issue claiming an identity another already holds, and the error names the issue that holds it. |

That uniqueness is the point of the feature rather than the clickable link: it is what lets an import run twice and update the issue it created the first time, instead of producing a duplicate. `Provenance` is the default because it claims nothing — a wrongly asserted `Identity` would block the issue that really is that ticket.

References travel on `CreateAsync` and `UpdateAsync` rather than through endpoints of their own, unlike links. A link is an edge between two issues and needs checking; a reference is a property of one issue. Note that `UpdateAsync` **replaces**, so omitting `References` clears them.

### Workflow

Each team has one workflow — a list of states plus the transitions allowed between them — seeded as `Todo → Doing → Done`. Read it with `GetWorkflowAsync`, replace it with `SetWorkflowAsync`. A state change goes through `SetStateAsync` rather than `UpdateAsync` so the move can be checked; a transition the workflow does not permit is rejected, and the error names the states the issue can actually reach.

A replacement workflow that would leave issues stranded in a state it no longer defines is rejected rather than applied.

#### Closing says why

A workflow has **one exit, not one per outcome**. `Todo → Doing → Done`, with the reason held on the
issue as a **resolution** — `Delivered`, `Won't do`, `No action`, or whatever else a team declares:

```csharp
await issueService.SetStateAsync(87, new SetIssueStateRequest
{
    State = "Done",
    Resolution = "Won't do"
});
```

`Resolution` is **required moving into a terminal state and rejected moving anywhere else**, and
reopening clears it — a closed issue with no reason is the ambiguity the field exists to remove, and
defaulting it to `Delivered` would quietly record a delivery that never happened.

The available resolutions come from the workflow, as `IssueWorkflowResolutionResponse { Name, Order,
IsSuccess }`. The **names are the team's own**, like state names; what is not is `IsSuccess`, which
says whether ending there counts as having delivered. That split is what lets the roadmap draw an
abandoned issue differently from a shipped one without recognising particular words — a renderer that
matched on `"Won't do"` would show any team with a different vocabulary as one flat colour. Declaring
them on the workflow rather than as a fixed enum also means an importer can hold an external
tracker's own term (GitHub's `not_planned`, Jira's `Won't Fix`) without a Toolkit release per source.

> **Reading these fields against an older server.** `Resolutions`, `IssueResponse.Resolution` and
> `RoadmapItemResponse.ResolutionIsSuccess` are all optional, because this package publishes before
> the server populates them. `ResolutionIsSuccess` is deliberately `bool?`: **`null` means there is
> nothing to judge** — the issue is open, or the server predates the field — and only an explicit
> `false` licenses drawing an ending as abandoned. Treating `null` as "did not deliver" would repaint
> every finished issue on every server that has not caught up.

#### Which phase it is in

Any non-terminal state may declare **sub-states**: the seed gives `Doing` the phases `In Progress`
and `To Deploy`. They are declared per state as
`IssueWorkflowStateResponse.SubStates`, and an issue carries its own as `IssueResponse.SubState`, with
`SubStateSinceUtc` saying how long it has been there.

```csharp
// Within the state the issue is already in: no transition, only the phase changes.
await issueService.SetStateAsync(87, new SetIssueStateRequest { State = "Doing", SubState = "To Deploy" });
```

A sub-state is **always optional** — an issue may hold none — and changing it needs no edge in the
workflow, because the state's rules (claims, board columns, transitions) stay with the state. Moving to
another state clears it unless the request names one of the target's. Naming the current state with
`SubState = null` leaves it alone, so an older client re-sending the state to take a claim cannot wipe
it; an empty string clears it. The terminal state has no sub-states of this kind: how an issue ended is
its resolution, which stays required.

#### Who is working on it

An issue in progress carries a **claim**: who says they are working on it, and from which machine.

```csharp
var issue = await issueService.GetAsync(99);
if (issue.Claim is { IsStale: false } claim)
{
    Console.WriteLine($"#{issue.Number} is held by {claim.User} on {claim.Machine}, last seen {claim.LastSeenUtc:u}");
}
```

The claim is **stated by the caller, not checked by the server**. A team API key carries no user, so
the server records what the `X-Quilt4Net-User` and `X-Quilt4Net-Machine` request headers say. A move
into an in-progress state takes the claim; a move to the entry state or to a terminal state releases
it; the holder's own writes keep `LastSeenUtc` fresh. Taking an issue somebody else holds succeeds with
a warning rather than failing, because the holder may simply have crashed.

`IsStale` is judged by the server — a claim whose holder has not written for a day — so every client
draws the same line between "somebody is on it" and "somebody was". `Claim` is optional and `null` when
nobody holds the issue, which is also how every issue reads against a server that predates claims.

#### What it is waiting for

An open issue can be **flagged as waiting**: held up by a person, a date, another issue or something
outside the team. The flag is not a state, so the issue keeps its state, phase and claim, and the
board can tell the work that moves from the work that waits.

```csharp
await issueService.SetWaitingAsync(87, new SetIssueWaitingRequest
{
    Reason = "Record October once the month has closed",
    WaitsOn = IssueWaitsOn.Date,
    Until = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc)
});

await issueService.ClearWaitingAsync(87);
```

The reason is required; `WaitsOn` and `Until` are optional. Once `Until` has passed the flag reads as
due (`IssueWaitingResponse.IsDue`, judged by the server); it is not cleared on its own, because somebody
has to confirm the wait is over. Closing an issue clears its flag, and the terminal state refuses one.
Moving between open states keeps it, so an issue put back in `Todo` with a date to resume carries that
date. An issue another open issue `Blocks` is *blocked*, which is a condition of its own and needs no
flag. `Waiting` is `null` when the issue is not flagged, which is also how every issue reads against an
older server.

`IssueFilter.Readiness` selects open work by that condition: `Blocked`, `Waiting` (flagged) and
`Ready` (neither). Nothing or all three selected shows everything; narrowed to some, finished issues
are left out.

### IssueOptions

| Property | Default | Description |
|----------|---------|-------------|
| `Quilt4NetAddress` | `https://quilt4net.com/` | Server base URL. |
| `ApiKey` | — *(required)* | Team API key. Reading needs `issue:read`; writing needs `issue:write`; replacing the workflow needs `issue:manage`. |

Configuration path: `Quilt4Net:Issue` (or top-level `Quilt4Net:ApiKey` + `Quilt4Net:Quilt4NetAddress` for shared keys).

### Behaviour and contract

- **Calls throw rather than degrade.** Unlike `IRemoteConfigurationService`, which serves a fallback when the server is unreachable, a tracker read that quietly returned an empty set would be indistinguishable from a team with no issues. Failures surface as `IssueServiceException` carrying the status code, and its message is the server's own explanation (the problem response's `detail`).
- **A 403 says which of two things it is.** The key lacks a scope, and the message names the scope; or the team has switched the Issues feature off, and the message names the feature. Only the team's owner or an administrator can switch it back on. `IssueRoadmap` shows that message in place of the roadmap.
- **A 409 means the issue kept changing while it was written**, by other writers at the same moment. Read it again and retry.
- **A 429 carries the server's `Retry-After`** on `IssueServiceException.RetryAfter`. Honour it; do not retry immediately.
- **`UpdateAsync` replaces, it does not merge.** Read the issue, change what you mean to change, and send the whole thing back. An omitted optional field is cleared.
- **Enums travel as names**, not numbers, so inserting a member never re-grades values already in flight.

### Minting a key

In the Quilt4Net.Server admin UI under **Api → Api Key**. `issue:read` comes with any key at Viewer level or above. **`issue:write` and `issue:manage` come only at Administrator level**, or with the **Issue Manager** role, so an application key that holds `content:write` cannot edit the tracker. For MCP access the key also needs `mcp:discover`.

## What every call sends

Alongside `X-API-KEY`, every request the Toolkit makes to Quilt4Net carries the Toolkit's own
identity:

```
X-Quilt4Net-Client: Quilt4Net.Toolkit/1.0.13
```

Nothing to configure — it is added wherever the API key is. It exists so the server can answer
**"which consumers are on which Toolkit version"** from data rather than assumption, which is what
makes it safe to retire an old contract: backward compatibility can be dropped once the record shows
nobody is still using it, and a consumer below the supported floor can be told so directly instead of
failing obscurely.

Build metadata is stripped, so the value is the released version rather than the exact commit —
otherwise the server's record would gain a row per build rather than per release, and the question
being asked is about released contracts.

> Sent on **every** request rather than through a one-time greeting on purpose. A greeting cannot
> carry enforcement: a server cannot refuse a call on the strength of a handshake that may never have
> happened, and a restart, a new process instance or a cached client skips it.

## Universal telemetry identity

`AddQuilt4NetLogging()` configures OpenTelemetry resource attributes **and** registers two `BaseProcessor`s — one for `LogRecord`, one for `Activity` — that copy a fixed set of identity attributes onto every per-record Properties bag. Works for all app types; the Azure Monitor exporter forwards the per-record attributes into `customDimensions`, where KQL can read them.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddQuilt4NetLogging();
```

Up to six attributes attached to every `AppTrace`, `AppException`, `AppRequest` (and outbound `AppDependency`):

| Attribute key | Default value | Notes |
|---|---|---|
| `service.name` | `IHostEnvironment.ApplicationName` → entry assembly name (framework assemblies excluded) | Also surfaces as the `cloud_RoleName` column. |
| `service.version` | Entry assembly version | Also surfaces as `application_Version`. |
| `host.name` | `Environment.MachineName` | Also surfaces as `cloud_RoleInstance` *unless* `ServiceInstanceId` is set (see below) — in which case the variant id wins for `cloud_RoleInstance` and `host.name` keeps the machine name in `customDimensions`. |
| `deployment.environment` | `IHostEnvironment.EnvironmentName` → `DOTNET_ENVIRONMENT` → `ASPNETCORE_ENVIRONMENT` → `"Production"` | The Azure Monitor exporter does **not** forward arbitrary OTel resource attributes into per-row `Properties`, so the per-record processor copies it in too. |
| `quilt4net.monitor` | `"Quilt4Net"` (configurable via `MonitorName`) | Identifies which instrumentation produced the row. Useful when several Quilt4Net-hosted services ship to the same workspace. |
| `service.instance.id` | *unset by default* — see "Distinguishing multiple deployments of the same binary" below. | Only emitted per-record when explicitly configured. Existing consumers see no new attribute. |

Override via callback or `appsettings.json`:

```csharp
builder.AddQuilt4NetLogging(o =>
{
    o.ApplicationName = "florida-server";
    o.Version = "2.0.0";
    o.Environment = "Production";
    o.MonitorName = "florida";
});
```

```json
{
  "Quilt4Net": {
    "Logging": {
      "ApplicationName": "florida-server",
      "Version": "2.0.0",
      "Environment": "Production",
      "MonitorName": "florida"
    }
  }
}
```

`AddQuilt4NetLogging()` returns a `Quilt4NetLoggingBuilder` that extension packages can chain off. `Quilt4Net.Toolkit.Api` adds `.AddHttpRequestLogging()` to enable HTTP request/response middleware including the `X-Correlation-ID` propagation scope:

```csharp
builder.AddQuilt4NetLogging()
    .AddHttpRequestLogging();
```

When `Quilt4Net.Toolkit.Api`'s `CorrelationIdMiddleware` is active, every `ILogger` call inside a request is scoped with the correlation id. That id reaches `customDimensions["CorrelationId"]` only when scope capture is enabled — set `IncludeScopes` (see below). See the Api package README.

### Exception data and log scopes

Two opt-in enrichers copy extra context onto the per-record `customDimensions`:

| Option | Default | Effect |
|---|---|---|
| `EnrichExceptionData` | **on** | Copies a logged exception's `Exception.Data` entries onto the exception telemetry. An id attached with `e.AddData("CorrelationId", guid)` becomes queryable in AI. |
| `IncludeScopes` | **off** | Captures `ILogger` scope values (e.g. the `CorrelationId` scope `CorrelationIdMiddleware` pushes) and copies them onto every record so they land in `customDimensions`. Opt-in because scope capture adds telemetry volume. |

```csharp
builder.AddQuilt4NetLogging(o =>
{
    o.IncludeScopes = true;      // scope values (incl. CorrelationId) → customDimensions
    // o.EnrichExceptionData = false;  // to opt out of Exception.Data enrichment
});
```

With `EnrichExceptionData` on, an exception carrying a correlation id is findable directly:

```csharp
catch (Exception e)
{
    e.AddData("CorrelationId", correlationId);   // Quilt4Net.Toolkit.Features.Measure
    logger.LogError(e, e.Message);
}
```

```kql
AppExceptions | where tostring(customDimensions.CorrelationId) == "<guid>"
```

> This `CorrelationId` (an id that flows across service hops and into `customDimensions`) is distinct from the user-facing 6-character `IncidentId` shown in Log-view error messages.

> Enrichment runs on the OpenTelemetry pipeline. Apps ingesting via the classic Application Insights SDK on AI 3.x must export logs through `Azure.Monitor.OpenTelemetry` to benefit — AI 3.x does not ingest `ILogger` telemetry through the classic pipeline.

### Distinguishing multiple deployments of the same binary

If the same compiled service is deployed under multiple logical names (multi-tenant / multi-brand / blue-green / shadow) and `service.name` is the same across them, telemetry alone can't tell the deployments apart — `host.name` only disambiguates the *machine*, not the *variant*. Set `ServiceInstanceId` so each row carries the deployment-variant identity:

```csharp
builder.AddQuilt4NetLogging(o =>
{
    o.ServiceInstanceId = builder.Configuration["DeploymentVariant"]; // e.g. "Thargelion"
});
```

When set, the value lands on the OTel resource (`cloud_RoleInstance` in Application Insights) **and** on `customDimensions["service.instance.id"]` for every record, so KQL can split rows by variant without portal lookups:

```kql
AppTraces
| where AppRoleName == 'Eplicta.FortDocs.Server'
| extend variant = tostring(todynamic(Properties)['service.instance.id'])
| summarize count() by variant, host = tostring(todynamic(Properties)['host.name'])
```

The toolkit's startup line also surfaces the variant when set:

```
Quilt4Net startup: Eplicta.FortDocs.Server [Thargelion] v1.2.9.0 in CI
```

Resolution precedence if `ServiceInstanceId` isn't passed in code:

1. `OTEL_RESOURCE_ATTRIBUTES` env var, parsed for the `service.instance.id=...` pair (the OTel-standard env var, [SDK env var spec](https://opentelemetry.io/docs/specs/otel/configuration/sdk-environment-variables/#general-sdk-configuration)).
2. `QUILT4NET_SERVICE_INSTANCE_ID` env var (Quilt4Net shorthand for hosts that don't want to construct the multi-key OTel string by hand).
3. *Unset* — falls back to today's behaviour (`cloud_RoleInstance` is `MachineName`; no per-record attribute).

### Registration order with other ILoggerProvider registrations

If you also use a non-OTel logger provider — e.g. `Microsoft.ApplicationInsights.AspNetCore`'s `AddApplicationInsightsTelemetry` — **and** wrap `ILoggerFactory` (e.g. for enrichment), call `AddQuilt4NetLogging()` **before** the other AI/OTel `ILoggerProvider` registration **and** before the factory wrap. Some shapes of "wrap that captures `sp.GetServices<ILoggerProvider>()` and rebuilds a `LoggerFactory`" interact with the OTel pipeline in a way that silently drops `AppTraces` when the order is reversed (`AppRequests` continue to flow because they're written via `TelemetryClient.TrackRequest` directly). Tracked in [issue #87](https://github.com/Quilt4/Quilt4Net.Toolkit/issues/87).

The recommended shape:

```csharp
// 1. Quilt4Net first.
builder.AddQuilt4NetLogging()
    .AddHttpRequestLogging();

// 2. Then the AI / other OTel provider.
builder.Services.AddApplicationInsightsTelemetry(o => { o.ConnectionString = "..."; });

// 3. Then any custom ILoggerFactory wrapping.
```

### Host and runtime metrics

`AddQuilt4NetLogging()` covers telemetry identity, logs, and traces — it does **not** emit host or process **metrics**. Host metrics (CPU, memory, disk space, network) belong to an OpenTelemetry Collector; .NET runtime/process metrics are available via standard OpenTelemetry instrumentation you can wire up yourself. See the docs article [Host and runtime metrics](../docs/articles/telemetry-identity.md#host-and-runtime-metrics) for how.

## Measure extensions

Extension methods on `ILogger` to measure and log execution time.

```csharp
// Measure synchronous work
_logger.Measure("ProcessOrder", () =>
{
    // work to measure
});

// Measure async work with result
var result = await _logger.MeasureAsync("FetchData", async () =>
{
    return await _repository.GetDataAsync();
});

// Log a count
_logger.Count("ItemsProcessed", items.Length);
```

## Logging attributes

Control HTTP request/response logging on endpoints.

```csharp
[Logging(RequestBody = true, ResponseBody = false)]
public async Task<IActionResult> StreamData() { ... }

[LoggingStream] // Shorthand for ResponseBody = false
public async Task<IActionResult> StreamEvents() { ... }
```

## Configuration

All options can be set via code or `appsettings.json`. Code takes priority.

```json
{
  "Quilt4Net": {
    "ApiKey": "YOUR_API_KEY_HERE",
    "HealthClient": {
      "HealthAddress": "https://my-service.example.com/api/Health/"
    },
    "ApplicationInsights": {
      "TenantId": "your-tenant-id",
      "WorkspaceId": "your-workspace-id",
      "ClientId": "your-client-id",
      "ClientSecret": "your-client-secret",
      "AuthMode": "ClientSecret"
    },
    "RemoteConfiguration": {
      "Ttl": "00:10:00"
    },
    "Content": {
      "Application": "MyApp"
    }
  }
}
```
