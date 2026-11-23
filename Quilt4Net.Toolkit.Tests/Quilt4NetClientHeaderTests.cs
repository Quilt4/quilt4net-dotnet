using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quilt4Net.Toolkit.Features.Issue;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Phase 1 of the compatibility mechanic: every call identifies the Toolkit that made it.
/// </summary>
/// <remarks>
/// Without this the server cannot answer "is anyone still on the old contract" except by
/// assumption, which is what the mechanic exists to replace.
/// </remarks>
public class Quilt4NetClientHeaderTests
{
    [Fact]
    public void The_header_carries_name_and_version()
    {
        Quilt4NetClient.Value.Should().StartWith("Quilt4Net.Toolkit/");
        Quilt4NetClient.Value.Split('/')[1].Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Build_metadata_never_reaches_the_wire()
    {
        // SourceLink appends +<commit> to the informational version. The server records what it
        // receives, so a per-commit value would give the ledger one row per build rather than one
        // per release - and the question being asked is about released contracts.
        typeof(Quilt4NetClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion
            .Should().Contain("+", "otherwise this test proves nothing about stripping it");

        Quilt4NetClient.Value.Should().NotContain("+");
    }

    [Fact]
    public void The_header_name_is_fixed()
    {
        // The server will branch on this and old clients cannot be re-taught, so it is effectively
        // permanent from the first release that sends it.
        Quilt4NetClient.HeaderName.Should().Be("X-Quilt4Net-Client");
    }

    [Fact]
    public void The_value_is_a_valid_http_header_value()
    {
        using var request = new HttpRequestMessage();
        var added = request.Headers.TryAddWithoutValidation(Quilt4NetClient.HeaderName, Quilt4NetClient.Value);

        added.Should().BeTrue();
        request.Headers.TryGetValues(Quilt4NetClient.HeaderName, out var values).Should().BeTrue();
        values!.Should().ContainSingle().Which.Should().Be(Quilt4NetClient.Value);
    }

    [Fact]
    public void A_registered_client_actually_sends_it()
    {
        // The helper being right is not the same as the registrations using it. This is the test
        // that would catch a patched-but-wrong lambda, which is the realistic mistake.
        var services = new ServiceCollection();
        services.AddQuilt4NetIssues(null, o => { o.Quilt4NetAddress = "https://example.com/"; o.ApiKey = "abc123"; });
        var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(IssueService.HttpClientName);

        client.DefaultRequestHeaders.GetValues(Quilt4NetClient.HeaderName)
            .Should().ContainSingle().Which.Should().Be(Quilt4NetClient.Value);
    }

    [Fact]
    public void It_is_not_sent_twice_when_registered_twice()
    {
        // Same hazard the X-API-KEY registration already guards: a doubled header is a different
        // value on the wire, and a server parsing it would see something neither client sent.
        var services = new ServiceCollection();
        services.AddQuilt4NetIssues(null, o => { o.Quilt4NetAddress = "https://example.com/"; o.ApiKey = "abc123"; });
        services.AddQuilt4NetIssues(null, o => { o.Quilt4NetAddress = "https://example.com/"; o.ApiKey = "abc123"; });
        var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(IssueService.HttpClientName);

        client.DefaultRequestHeaders.GetValues(Quilt4NetClient.HeaderName).Should().ContainSingle();
    }
}
