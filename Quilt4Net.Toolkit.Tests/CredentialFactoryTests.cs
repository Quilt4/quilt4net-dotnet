using Azure.Identity;
using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class CredentialFactoryTests
{
    [Fact]
    public void ClientSecret_mode_with_secret_returns_ClientSecretCredential()
    {
        var credential = CredentialFactory.Create(
            ApplicationInsightsAuthMode.ClientSecret,
            tenantId: "tenant-guid",
            clientId: "client-guid",
            clientSecret: "secret-value");

        credential.Should().BeOfType<ClientSecretCredential>();
    }

    [Fact]
    public void ClientSecret_mode_without_secret_throws_with_helpful_message()
    {
        var act = () => CredentialFactory.Create(
            ApplicationInsightsAuthMode.ClientSecret,
            tenantId: "tenant-guid",
            clientId: "client-guid",
            clientSecret: null);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("ClientSecret").And.Contain("ManagedIdentity");
    }

    [Fact]
    public void ManagedIdentity_mode_without_clientId_returns_system_assigned_credential()
    {
        var credential = CredentialFactory.Create(
            ApplicationInsightsAuthMode.ManagedIdentity,
            tenantId: null,
            clientId: null,
            clientSecret: null);

        credential.Should().BeOfType<ManagedIdentityCredential>();
    }

    [Fact]
    public void ManagedIdentity_mode_with_clientId_returns_user_assigned_credential()
    {
        // User-assigned MI: client id is the identity's client id, not an app registration.
        var credential = CredentialFactory.Create(
            ApplicationInsightsAuthMode.ManagedIdentity,
            tenantId: null,
            clientId: "11111111-1111-1111-1111-111111111111",
            clientSecret: null);

        credential.Should().BeOfType<ManagedIdentityCredential>();
    }

    [Fact]
    public void ManagedIdentity_mode_does_not_require_ClientSecret()
    {
        // No throw — MI flow must not depend on a secret being set.
        var act = () => CredentialFactory.Create(
            ApplicationInsightsAuthMode.ManagedIdentity,
            tenantId: null,
            clientId: null,
            clientSecret: null);

        act.Should().NotThrow();
    }

    [Fact]
    public void DefaultAzureCredential_mode_returns_DefaultAzureCredential()
    {
        var credential = CredentialFactory.Create(
            ApplicationInsightsAuthMode.DefaultAzureCredential,
            tenantId: "tenant-guid",
            clientId: "11111111-1111-1111-1111-111111111111",
            clientSecret: null);

        credential.Should().BeOfType<DefaultAzureCredential>();
    }

    [Fact]
    public void DefaultAzureCredential_mode_works_with_no_tenant_or_client()
    {
        // Local-dev story: developer has run `az login` and has nothing else configured.
        // Empty TenantId / ClientId must fall through to the SDK's own discovery.
        var act = () => CredentialFactory.Create(
            ApplicationInsightsAuthMode.DefaultAzureCredential,
            tenantId: null,
            clientId: null,
            clientSecret: null);

        act.Should().NotThrow();
    }
}
