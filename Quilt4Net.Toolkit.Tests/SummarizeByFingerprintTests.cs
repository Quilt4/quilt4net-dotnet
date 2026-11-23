using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// The summary list KQL buckets by <c>(Fingerprint, Application)</c> so each application that
/// produced a fingerprint gets its own application-scoped row (matching the drill-down). Two things
/// are pinned here: <c>Application</c> IS part of the key (issue #126 — otherwise counts and the
/// instance list span every application), and <c>Message</c> is NOT (it varies per formatted trace
/// line and blew past Kusto's 64MB result-set limit when it was in the key).
/// </summary>
public class SummarizeByFingerprintTests
{
    [Fact]
    public void Buckets_by_fingerprint_and_application()
    {
        ApplicationInsightsService.SummarizeByFingerprint
            .Should().Contain("by Fingerprint, Application")
            .And.NotContain("by Fingerprint, Message");
    }

    [Fact]
    public void Keeps_message_out_of_the_key_to_avoid_the_64mb_blowup()
    {
        // Message must remain a representative sample, never a grouping key.
        ApplicationInsightsService.SummarizeByFingerprint
            .Should().Contain("Message = take_any(Message)");
    }

    [Fact]
    public void Takes_a_representative_sample_for_non_key_descriptive_columns()
    {
        // Environment stays a sample (already filtered upstream); Application is now a key, so it is
        // no longer aggregated via take_any.
        ApplicationInsightsService.SummarizeByFingerprint
            .Should().Contain("Environment = take_any(Environment)")
            .And.NotContain("Application = take_any(Application)");
    }

    [Fact]
    public void Aggregates_severity_to_the_max_observed()
    {
        ApplicationInsightsService.SummarizeByFingerprint
            .Should().Contain("SeverityLevel = max(SeverityLevel)");
    }

    [Fact]
    public void Counts_occurrences_and_tracks_last_seen()
    {
        ApplicationInsightsService.SummarizeByFingerprint
            .Should().Contain("Count = count()")
            .And.Contain("LastTimeGenerated = max(TimeGenerated)");
    }

    [Fact]
    public void Orders_results_most_recent_first()
    {
        ApplicationInsightsService.SummarizeByFingerprint
            .Should().Contain("order by LastTimeGenerated desc");
    }
}
