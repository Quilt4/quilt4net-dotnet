using System.Reflection;
using AutoFixture;
using AwesomeAssertions;
using Microsoft.Extensions.Hosting;
using Moq;
using Quilt4Net.Toolkit.Features.Api;
using Quilt4Net.Toolkit.Features.Health.Version;
using Xunit;

namespace Quilt4Net.Toolkit.Api.Tests;

public class VersionServiceTests
{
    [Fact]
    public async Task Basic()
    {
        //Arrange
        var environmentName = new Fixture().Create<string>();
        var hostEnvironment = new Mock<IHostEnvironment>(MockBehavior.Strict);
        hostEnvironment.SetupGet(x => x.EnvironmentName).Returns(environmentName);
        var sut = new VersionService(hostEnvironment.Object, new Quilt4NetHealthApiOptions());

        //Act
        var result = await sut.GetVersionAsync(TestContext.Current.CancellationToken);

        //Assert
        result.Should().NotBeNull();
        result.Environment.Should().Be(environmentName);
        result.Machine.Should().Be(Environment.MachineName);
    }

    // Issue #110: AssemblyVersion is a 4-part numeric and can't carry "-pre.n". A SemVer-aware
    // field has to come from AssemblyInformationalVersionAttribute, with SourceLink's optional
    // "+commit-hash" build metadata stripped so the value is clean SemVer.

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.4.12-pre.1", "1.4.12-pre.1")]
    [InlineData("1.4.12+abc123", "1.4.12")]
    [InlineData("1.4.12-pre.1+abc123", "1.4.12-pre.1")]
    public void StripBuildMetadata_returns_SemVer_without_plus_suffix(string input, string expected)
    {
        VersionService.StripBuildMetadata(input).Should().Be(expected);
    }

    [Fact]
    public void ReadInformationalVersion_returns_null_for_null_assembly()
    {
        VersionService.ReadInformationalVersion(null).Should().BeNull();
    }

    [Fact]
    public void ReadInformationalVersion_reads_attribute_from_assembly()
    {
        // The Quilt4Net.Toolkit assembly is built with GenerateDocumentationFile + the
        // default MSBuild version pipeline, so AssemblyInformationalVersionAttribute is
        // always present. The value should match whatever MSBuild stamped, after the
        // "+commit-hash" SourceLink suffix is stripped — so use StripBuildMetadata as the
        // oracle. Locks behaviour without coupling the test to a specific version literal.
        var asm = typeof(VersionService).Assembly;
        var expected = VersionService.StripBuildMetadata(
            asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

        VersionService.ReadInformationalVersion(asm).Should().Be(expected);
    }
}