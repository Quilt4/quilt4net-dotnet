using AwesomeAssertions;
using Quilt4Net.Toolkit.Blazor.Features.Log;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class LogNavParamsTests
{
    [Fact]
    public void Encode_Then_Decode_RoundTrips_AllProperties()
    {
        // Arrange
        var original = new LogNavParams
        {
            Environment = "Production",
            RangeMinutes = 1440,
            Source = LogSource.Trace.ToString(),
            Context = "someContextKey",
            Reference = "Search"
        };

        // Act
        var encoded = original.Encode();
        var decoded = LogNavParams.Decode(encoded);

        // Assert
        decoded.Environment.Should().Be(original.Environment);
        decoded.RangeMinutes.Should().Be(original.RangeMinutes);
        decoded.Source.Should().Be(original.Source);
        decoded.Context.Should().Be(original.Context);
        decoded.Reference.Should().Be(original.Reference);
    }

    [Fact]
    public void Encode_ProducesUrlSafeString()
    {
        // Arrange
        var p = new LogNavParams
        {
            Environment = "Test",
            RangeMinutes = 60,
            Source = LogSource.Exception.ToString(),
            Context = "ctx",
            Reference = "Detail"
        };

        // Act
        var encoded = p.Encode();

        // Assert - must not contain standard base64 chars that are unsafe in URLs
        encoded.Should().NotContain("+");
        encoded.Should().NotContain("/");
        encoded.Should().NotContain("=");
    }

    [Fact]
    public void Decode_EmptyString_ReturnsDefaultInstance()
    {
        // Act
        var result = LogNavParams.Decode(string.Empty);

        // Assert
        result.Should().NotBeNull();
        result.Environment.Should().BeNull();
        result.RangeMinutes.Should().Be(0);
        result.Source.Should().BeNull();
        result.Context.Should().BeNull();
    }

    [Fact]
    public void Decode_NullString_ReturnsDefaultInstance()
    {
        // Act
        var result = LogNavParams.Decode(null);

        // Assert
        result.Should().NotBeNull();
        result.Environment.Should().BeNull();
    }

    [Fact]
    public void From_SetsAllProperties()
    {
        // Arrange
        var environment = "Staging";
        var range = TimeSpan.FromHours(3);
        var source = LogSource.Trace;

        // Act
        var p = LogNavParams.From(environment, range, source, null, "Count");

        // Assert
        p.Environment.Should().Be(environment);
        p.RangeMinutes.Should().Be(range.TotalMinutes);
        p.Source.Should().Be(source.ToString());
        p.Context.Should().BeNull();
        p.Reference.Should().Be("Count");
    }

    [Fact]
    public void GetRange_ReturnsCorrectTimeSpan()
    {
        // Arrange
        var p = new LogNavParams { RangeMinutes = 90 };

        // Act & Assert
        p.GetRange().Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void GetSource_ReturnsCorrectEnum()
    {
        // Arrange
        var p = new LogNavParams { Source = LogSource.Exception.ToString() };

        // Act & Assert
        p.GetSource().Should().Be(LogSource.Exception);
    }

    [Fact]
    public void GetSource_NullSource_ReturnsNull()
    {
        // Arrange
        var p = new LogNavParams { Source = null };

        // Act & Assert
        p.GetSource().Should().BeNull();
    }

    [Fact]
    public void Encode_Decode_WithSpecialCharactersInEnvironment_RoundTrips()
    {
        // Arrange - special chars that would normally cause issues in base64 or URLs
        var original = new LogNavParams
        {
            Environment = "my env/test+special=chars",
            RangeMinutes = 30,
            Source = LogSource.Trace.ToString(),
            Context = null,
            Reference = "Search"
        };

        // Act
        var encoded = original.Encode();
        var decoded = LogNavParams.Decode(encoded);

        // Assert
        decoded.Environment.Should().Be(original.Environment);
    }

    [Fact]
    public void Application_RoundTrips_Through_From_And_Encode_Decode()
    {
        // #126: the selected application must survive the navigation round-trip so the summary
        // route scopes the drill-down to the same application the row represented.
        var p = LogNavParams.From("Production", TimeSpan.FromDays(1), LogSource.Trace, null, "Detail", application: "Eplicta.Aggregator");
        p.Application.Should().Be("Eplicta.Aggregator");

        var decoded = LogNavParams.Decode(p.Encode());
        decoded.Application.Should().Be("Eplicta.Aggregator");
    }
}
