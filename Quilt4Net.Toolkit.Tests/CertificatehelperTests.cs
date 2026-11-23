using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.Health;
using Quilt4Net.Toolkit.Features.Health.Dependency;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class CertificatehelperTests
{
    [Theory]
    [InlineData("http://localhost:5232")]
    [InlineData("http://example.com")]
    [InlineData("http://127.0.0.1:8080")]
    public async Task GetCertificateHealthAsync_Returns_Healthy_For_Http_Scheme(string url)
    {
        var uri = new Uri(url);

        var result = await Certificatehelper.GetCertificateHealthAsync(uri, null);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Details.Should().ContainKey("scheme");
        result.Details["scheme"].Should().Be("http");
        result.Details.Should().NotContainKey("certificateExpiryDate");
    }

    [Fact]
    public async Task GetCertificateInfoAsync_Returns_None_Tls_For_Http_Scheme()
    {
        // Previously attempted TLS handshake and threw AuthenticationException
        // ("Cannot determine the frame size") when called with a plain http:// URI.
        var uri = new Uri("http://localhost:5232");

        var (tlsVersion, expiry, host) = await Certificatehelper.GetCertificateInfoAsync(uri);

        tlsVersion.Should().Be(System.Security.Authentication.SslProtocols.None);
        expiry.Should().BeNull();
        host.Should().Be("localhost");
    }
}
