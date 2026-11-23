using AwesomeAssertions;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class Class1
{
    [Fact]
    public void A()
    {
        true.Should().BeTrue();
    }
}