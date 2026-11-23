using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Features.Issue;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// A library owns the registration of its own services — a consumer must never have to enumerate a
/// library's interfaces by hand to make it work. These tests pin that <c>AddQuilt4NetIssues</c>
/// registers the complete set, because a Blazor <c>@inject</c> is resolved at render time and a
/// missing registration surfaces as a broken page rather than a failed startup.
/// </summary>
public class IssueRegistrationTests
{
    [Fact]
    public void AddQuilt4NetIssues_registers_everything_the_component_injects()
    {
        var provider = Build();

        provider.GetService<IIssueService>().Should().NotBeNull();
        provider.GetService<IConnectionService>().Should().NotBeNull("IssueRoadmap wraps itself in ConnectionWrapper");
        provider.GetService<IHttpClientFactory>().Should().NotBeNull();
        provider.GetService<IOptions<IssueOptions>>().Should().NotBeNull();
    }

    [Fact]
    public void The_named_http_client_carries_the_address_and_the_api_key()
    {
        var provider = Build(address: "https://example.com/", apiKey: "abc123");

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(IssueService.HttpClientName);

        client.BaseAddress.Should().Be(new Uri("https://example.com/"));
        client.DefaultRequestHeaders.GetValues("X-API-KEY").Should().ContainSingle().Which.Should().Be("abc123");
    }

    [Fact]
    public void The_api_key_is_not_sent_twice_when_registered_twice()
    {
        // The server rejects a doubled X-API-KEY as an invalid key, so a duplicate registration must
        // stay idempotent rather than producing a 401 that looks like a bad credential.
        var services = new ServiceCollection();
        services.AddQuilt4NetIssues(null, o => { o.Quilt4NetAddress = "https://example.com/"; o.ApiKey = "abc123"; });
        services.AddQuilt4NetIssues(null, o => { o.Quilt4NetAddress = "https://example.com/"; o.ApiKey = "abc123"; });
        var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(IssueService.HttpClientName);

        client.DefaultRequestHeaders.GetValues("X-API-KEY").Should().ContainSingle();
    }

    [Fact]
    public void An_unparseable_address_fails_at_registration_rather_than_at_first_call()
    {
        var services = new ServiceCollection();

        var act = () => services.AddQuilt4NetIssues(null, o => o.Quilt4NetAddress = "not-a-uri");

        act.Should().Throw<InvalidOperationException>().WithMessage("*not-a-uri*");
    }

    [Fact]
    public void The_address_defaults_so_an_unbound_options_object_is_still_usable()
    {
        new IssueOptions().Quilt4NetAddress.Should().Be("https://quilt4net.com/");
    }

    private static ServiceProvider Build(string address = "https://example.com/", string apiKey = "key")
    {
        var services = new ServiceCollection();
        services.AddQuilt4NetIssues(null, o =>
        {
            o.Quilt4NetAddress = address;
            o.ApiKey = apiKey;
        });
        return services.BuildServiceProvider();
    }
}
