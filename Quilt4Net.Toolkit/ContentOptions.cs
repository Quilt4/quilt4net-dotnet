namespace Quilt4Net.Toolkit;

/// <summary>
/// This option can be configured by code or with appsettings.json on location "Quilt4Net/Content"
/// </summary>
public record ContentOptions
{
    /// <summary>
    /// Name of the application that will be used in Quilt4Net.
    /// Default is the name of the assembly.
    /// </summary>
    public string Application { get; set; }

    /// <summary>
    /// Address to the Quilt4Net server.
    /// Default is https://quilt4net.com/. Defaulted on the type so an unbound
    /// <c>IOptions&lt;ContentOptions&gt;</c> still carries a usable URL when only
    /// part of the toolkit is registered (e.g. <c>AddQuilt4NetRemoteConfiguration</c>
    /// without <c>AddQuilt4NetContent</c>).
    /// </summary>
    public string Quilt4NetAddress { get; set; } = "https://quilt4net.com/";

    /// <summary>
    /// Api key to be used for calls to the server.
    /// This key can be retrieved from https://quilt4net.com/.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// How long to stop calling for a key after a <b>failed</b> call — a timeout, a transport error
    /// or a non-success status other than 404. Doubles per consecutive failure up to
    /// <see cref="MaxFailureCacheDuration"/> and resets on the first success.
    /// Default is 5 seconds.
    /// </summary>
    /// <remarks>
    /// This option previously had no effect for any key that had ever succeeded: the failure path
    /// preferred the last successful response's TTL, which is a content-freshness interval and has
    /// nothing to do with how long a fault should be believed. That is what pinned a value to its
    /// fallback for a full cache lifetime per failed attempt (issue #174), so the preference is
    /// gone and this value now governs the failure hold-off outright.
    /// <para>
    /// A <c>429</c> carrying <c>Retry-After</c> still wins over both this and the back-off — the
    /// server saying when it will be ready beats any local guess.
    /// </para>
    /// </remarks>
    public TimeSpan FailureCacheDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Ceiling for the consecutive-failure back-off described on <see cref="FailureCacheDuration"/>.
    /// A sustained outage settles here instead of retrying every few seconds per key.
    /// Default is 5 minutes.
    /// </summary>
    public TimeSpan MaxFailureCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long to remember that the server answered <c>404</c> for a key — i.e. there is no content
    /// override and the caller's default stands.
    /// Default is 10 minutes.
    /// </summary>
    /// <remarks>
    /// Deliberately not the failure back-off: a 404 is an <b>answer</b>, not a fault. The server was
    /// reached and replied. Holding it for seconds rather than minutes would re-request every
    /// unseeded key on nearly every render, which is the request flood this feature exists to
    /// remove. Where a previous successful response TTL is known for the key, that is used instead.
    /// </remarks>
    public TimeSpan NotFoundCacheDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Timeout for HTTP calls to the Quilt4Net server.
    /// Default is 5 seconds. When a stale cached value exists and <see cref="StaleWhileRevalidate"/>
    /// is enabled, the caller gets the stale value immediately and the refresh happens in the
    /// background, so this timeout only blocks when no cached value exists. When
    /// <see cref="StaleWhileRevalidate"/> is disabled, an expired entry is refreshed synchronously
    /// and this timeout applies to that call.
    /// </summary>
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// When true (default), an expired cache entry is returned immediately and refreshed in the
    /// background (stale-while-revalidate) — fast, but the caller may see one slightly stale value.
    /// When false, an expired entry is refreshed synchronously so the caller always gets a fresh
    /// value (subject to <see cref="HttpTimeout"/>), at the cost of blocking on the refresh.
    /// </summary>
    public bool StaleWhileRevalidate { get; set; } = true;

    /// <summary>
    /// How long a caller waits for a key the server has never seen before giving up and rendering
    /// its own declared default. The materialization still completes in the background, so the next
    /// render shows the stored value. Default is 200 ms. <see cref="TimeSpan.Zero"/> disables the
    /// bounded wait, restoring the previous behaviour of waiting up to <see cref="HttpTimeout"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because the first request for a new key is a <b>write</b>, not a read: the caller
    /// supplies the text, the server stores it, and the caller waits out the storing. Measured at
    /// 1.5–3.3 s per key, which is what turned a dialog declaring thirteen new keys into an
    /// eighteen-second first open, and 106 ms on every open after (Toolkit issue #182).
    /// </para>
    /// <para>
    /// The caller already holds the correct text — <c>defaultValue</c> and <c>translations</c> are
    /// literals in its own source — so waiting for the server to write down an answer it was just
    /// given buys nothing. What it shows meanwhile is the text it supplied for the selected language
    /// when there is one (matched by name, as the server matches it), and <c>defaultValue</c> otherwise.
    /// </para>
    /// <para>
    /// ⚠️ <b>The trade.</b> This applies to any key not in the cache, not only to genuinely new
    /// ones, because the client cannot tell the two apart until the server answers. For an existing
    /// key whose cache is cold, a slow server therefore means one render of the code's own text before
    /// the stored value arrives — briefly the code's wording rather than the edited one, and the default
    /// language when the code supplies no text for the selected one, where previously the render blocked.
    /// In practice the warm-up has already loaded every existing key, so the cold case is close to
    /// exclusively new keys. Set this to <see cref="TimeSpan.Zero"/> if a correct first paint
    /// matters more than a responsive one.
    /// </para>
    /// </remarks>
    public TimeSpan MaterializationWait { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Cache lifetime to request from the server. When <c>null</c> (the default) the server's own
    /// configured lifetime applies, as before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Issue #163: remote configuration has always been able to request a lifetime — <c>GetAsync</c>
    /// and <c>GetToggleAsync</c> take a <c>ttl</c> — and content could not. Content that changes a few
    /// times a week was expiring on the server's default of ten minutes.
    /// </para>
    /// <para>
    /// This composes with the periodic re-warm rather than replacing it:
    /// <see cref="WarmUpRefreshFraction"/> is applied to whatever lifetime the <b>server</b> reports,
    /// so asking for 24 hours turns a bulk call every 8 minutes into one every 19.2 hours with nothing
    /// else to change.
    /// </para>
    /// <para>
    /// The server clamps this to its own maximum, so a value it considers unreasonable is reduced
    /// rather than refused. <b>The trade-off is visibility of edits</b>: content changed on the server
    /// takes up to this long to reach an instance that is not restarted or reloaded — "Reload Content"
    /// in the admin UI remains the immediate path.
    /// </para>
    /// </remarks>
    public TimeSpan? CacheDuration { get; set; }

    /// <summary>
    /// When true (default), the Blazor content registration runs a startup warm-up that pre-fills
    /// the cache with the default language in one bulk call (so pages render without a request per
    /// key). The user's selected language is warmed per-circuit when it differs from the default.
    /// Set false to disable warm-up and rely solely on lazy per-key fetching.
    /// </summary>
    public bool WarmUpEnabled { get; set; } = true;

    /// <summary>
    /// When true (default), the bulk warm-up repeats on a timer rather than running once per
    /// process, so warmed entries are replaced shortly <i>before</i> they expire and the per-key
    /// path is never reached in steady state. Ignored when <see cref="WarmUpEnabled"/> is false.
    /// </summary>
    /// <remarks>
    /// Without this the bulk endpoint effectively serves the first cache lifetime of a process's
    /// life and little else: every warmed key shares one <c>ValidTo</c>, so the whole set expires at
    /// the same instant and the next render fans out one HTTP call per key — hundreds at once, which
    /// is what trips a server's per-caller limit and starts the timeout loop described in issue #163.
    /// </remarks>
    public bool PeriodicWarmUpEnabled { get; set; } = true;

    /// <summary>
    /// Where in the observed cache lifetime the periodic re-warm runs, as a fraction. Default is
    /// <c>0.8</c> — a re-warm at 80% of the lifetime, leaving headroom for a slow or failed pass
    /// before anything expires. Clamped to a sane range, and never faster than 30 seconds.
    /// </summary>
    public double WarmUpRefreshFraction { get; set; } = 0.8;

    /// <summary>
    /// Floor for the periodic re-warm interval, whatever <see cref="WarmUpRefreshFraction"/> and the
    /// server's lifetime work out to. Default is 30 seconds — a bulk call is cheap, but a server
    /// reporting a very short lifetime must not turn the re-warm into its own source of load.
    /// </summary>
    public TimeSpan MinimumWarmUpInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Additional languages to warm at startup (and on "Reload Content"), on top of the default
    /// language which is always warmed. Identified by <b>language name</b> exactly as entered on the
    /// server (e.g. <c>["English", "Svenska"]</c>), matching the naming convention used elsewhere in
    /// the content API. A name with no matching server language is skipped with a <c>Warning</c>.
    /// Empty (the default) preserves the previous behaviour — only the default language is warmed at
    /// startup, and other languages warm per-circuit when first selected. Ignored when
    /// <see cref="WarmUpEnabled"/> is false.
    /// </summary>
    public IReadOnlyList<string> WarmUpLanguages { get; set; } = [];

    /// <summary>
    /// When true (default), content resolutions are published as metrics on the
    /// <c>Quilt4Net.Toolkit.Content</c> meter — a resolution counter and a duration histogram, both
    /// tagged by source, so a host gets call volume, latency and cache-hit ratio without enabling
    /// <c>Debug</c> logging.
    /// </summary>
    /// <remarks>
    /// Defaults on because the cost is near zero when nobody subscribes to the meter, and because the
    /// number that matters most — the cache-hit ratio — cannot be computed outside this library: the
    /// public read returns a bare string, so a wrapper can time a call and never learn whether it was
    /// served from cache, stale cache, the server or a fallback.
    /// </remarks>
    public bool MetricsEnabled { get; set; } = true;

    /// <summary>
    /// Roles that grant content admin access (edit, debug, reload).
    /// Checked against the authenticated user's claim roles (e.g. Entra ID).
    /// Default is ["ContentAdmin", "Developer"].
    /// </summary>
    public string[] AdminRoles { get; set; } = ["ContentAdmin", "Developer"];

    /// <summary>
    /// When true, always grant content admin access regardless of authentication state.
    /// Useful during development when no identity provider is configured.
    /// Default is false.
    /// </summary>
    public bool AssumeAdmin { get; set; }

    /// <summary>
    /// Diagnostics (issue #132): when a content fetch or language-list load from the server takes
    /// at least this long, a single <c>Warning</c> is logged naming the endpoint, elapsed time and
    /// HTTP status — so slow content/language loads surface in production even when <c>Debug</c>
    /// logging is off. The detailed per-resolution timing lines (key, resolved language, cache
    /// hit/miss, source, elapsed) are logged at <c>Debug</c> on category
    /// <c>Quilt4Net.Toolkit.Features.Content.RemoteContentCallService</c> and are opt-in via normal
    /// log configuration. Set <see cref="TimeSpan.Zero"/> to disable the slow-load warning.
    /// Default is 3 seconds.
    /// </summary>
    public TimeSpan SlowLogThreshold { get; set; } = TimeSpan.FromSeconds(3);
}