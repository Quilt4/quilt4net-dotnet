using Quilt4Net.Toolkit;
using Quilt4Net.Toolkit.Api;
using Quilt4Net.Toolkit.Blazor;
using Quilt4Net.Toolkit.Blazor.Server.Sample.Components;
using Quilt4Net.Toolkit.Health;
using Radzen;
using Serilog;
using Serilog.Events;
using Tharga.Blazor.Framework;
// `using Serilog;` also brings Serilog.ILogger into scope, which collides with the
// Microsoft.Extensions.Logging.ILogger used by the correlation-demo endpoint below. Alias the
// unqualified name back to the MS type; Serilog is referenced via its own qualified names.
using ILogger = Microsoft.Extensions.Logging.ILogger;

var builder = WebApplication.CreateBuilder(args);

// Serilog file sink — a plain, tailable log on disk so you can see exactly what the content client
// is doing (which key resolved from where, when it fell back to a default, when translation is
// missing) without an App Insights round-trip. Added as an ADDITIONAL logging provider via
// builder.Logging.AddSerilog (not builder.Host.UseSerilog, which would clear the existing OTel/AI
// providers that AddQuilt4NetLogging sets up). Both sinks receive every log line.
// The content categories are raised to Debug in appsettings.Development.json so the per-resolution
// source lines reach this file. logs/ is git-ignored (*.log).
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.File(
        "logs/sample-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Logging.AddSerilog(Log.Logger, dispose: true);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Registers Radzen's DialogService / NotificationService / ContextMenuService / TooltipService.
// The visual hosts (RadzenDialog / RadzenNotification / ...) live in MainLayout.razor.
builder.Services.AddRadzenComponents();

// Tharga.Blazor's BreadCrumbService (used by <BreadCrumbs /> in NavMenu and by the toolkit's
// IPageBreadcrumbAdapter when the page reader pushes its ancestor chain). No team service
// wired up since the Sample doesn't have auth — pass the bare minimum config.
builder.Services.AddThargaBlazor(o =>
{
    o.Title = "Quilt4Net Blazor Sample";
});

builder.AddQuilt4NetBlazorContent(o =>
{
    o.AssumeAdmin = true;
    // Hot-load English + Svenska at startup (and on "Reload Content"), on top of the always-warmed
    // default language. Names must match the languages configured on the target Quilt4Net server.
    o.WarmUpLanguages = ["English", "Svenska"];
});

builder.AddQuilt4NetApplicationInsightsClient();
builder.AddQuilt4NetRemoteConfiguration();

// The rest of what the Quilt4Net panel (/quilt4net) shows: this instance's heartbeat, sending a
// notification (needs a key with notification:write; the tab hides without it) and health checks.
builder.AddQuilt4NetHeartbeatReporting(o => o.Capabilities = ["sample"]);
builder.AddQuilt4NetNotifications();
builder.AddQuilt4NetHealth();

// Register the per-record telemetry-identity processors (env / app / version / host /
// quilt4net.monitor) and the request middleware that mints / honors X-Correlation-ID.
// AddHttpRequestLogging is the opt-in that enables the middleware via UseQuilt4NetLogging.
// IncludeScopes is what carries the middleware's CorrelationId scope into customDimensions
// (see the /api/correlation-demo endpoint below).
builder.AddQuilt4NetLogging(o => o.IncludeScopes = true)
    .AddHttpRequestLogging();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseQuilt4NetLogging();
app.UseAntiforgery();

// Demo endpoint showing CorrelationId propagation across multiple log lines from a single
// request. Hit GET /api/correlation-demo (optionally with header X-Correlation-ID: my-id)
// — every ILogger call inside this request handler emits an AppTrace whose
// customDimensions["CorrelationId"] holds the same value, because CorrelationIdMiddleware
// wraps the pipeline in Logger.BeginScope for the duration of the request.
app.MapGet("/api/correlation-demo", (HttpContext ctx, ILogger<Program> logger) =>
{
    logger.LogInformation("Correlation demo: step 1 — request received");
    DoSomeWork(logger);
    logger.LogInformation("Correlation demo: step 3 — finishing");
    return Results.Ok(new
    {
        CorrelationId = ctx.Items["CorrelationId"]?.ToString(),
        Hint = "Send X-Correlation-ID on the request to chain across services. " +
               "Every log line in this handler shares the value; query AI with " +
               "customDimensions['CorrelationId'] == '<id>' to see the chain."
    });

    static void DoSomeWork(ILogger logger)
    {
        logger.LogInformation("Correlation demo: step 2 — doing work");
    }
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
