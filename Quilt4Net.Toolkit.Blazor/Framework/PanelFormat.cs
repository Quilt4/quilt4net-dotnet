namespace Quilt4Net.Toolkit.Blazor.Framework;

/// <summary>Short wording for the panel's times and states (#144). Relative, so it needs no time zone.</summary>
internal static class PanelFormat
{
    public static string Ago(DateTime? utc, DateTime? nowUtc = null)
        => utc is { } t ? Span((nowUtc ?? DateTime.UtcNow) - t) + " ago" : "never";

    public static string In(DateTime? utc, DateTime? nowUtc = null)
    {
        if (utc is not { } t) return "-";
        var left = t - (nowUtc ?? DateTime.UtcNow);
        return left <= TimeSpan.Zero ? "now" : "in " + Span(left);
    }

    public static string Span(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalSeconds < 60) return $"{(int)span.TotalSeconds} s";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min";
        if (span.TotalHours < 48) return $"{(int)span.TotalHours} h";
        return $"{(int)span.TotalDays} days";
    }

    public static string Clip(string value, int length = 120)
        => string.IsNullOrEmpty(value) || value.Length <= length ? value : value[..length] + "…";

    /// <summary>A metric's value with its unit: <c>41 %</c>, <c>812 MB</c>, or the plain number.</summary>
    public static string Metric(double value, string unit) => unit switch
    {
        "percent" => $"{value:0.#} %",
        "bytes" => value >= 1 << 30 ? $"{value / (1 << 30):0.#} GB" : $"{value / (1 << 20):0} MB",
        "ms" or "milliseconds" => $"{value:0} ms",
        null or "" => $"{value:0.##}",
        _ => $"{value:0.##} {unit}"
    };
}
