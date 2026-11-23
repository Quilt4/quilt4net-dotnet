using AwesomeAssertions;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class AssemblyScanTests
{
    // Guardrail: reproduces what any assembly-scanner (e.g. Tharga.Cache) does.
    // Regressed in 0.6.17 when Quilt4Net.Toolkit.Health bumped AI to 3.x and the
    // unified resolve removed ITelemetryInitializer from under Quilt4NetTelemetryInitializer.
    [Fact]
    public void Quilt4NetToolkit_CanEnumerateTypes()
    {
        var types = typeof(Quilt4Net.Toolkit.LoggingRegistration).Assembly.GetTypes();
        types.Should().NotBeEmpty();
    }
}
