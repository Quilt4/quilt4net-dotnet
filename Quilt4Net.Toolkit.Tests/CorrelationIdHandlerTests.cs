using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quilt4Net.Toolkit;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class CorrelationIdHandlerTests
{
    private sealed class StubAccessor : ICorrelationIdAccessor
    {
        public string Current { get; set; }
    }

    // Terminal handler that records the request it was asked to send, so we can assert on headers.
    private sealed class CapturingInnerHandler : HttpMessageHandler
    {
        public HttpRequestMessage LastRequest { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private static async Task<HttpRequestMessage> SendThrough(ICorrelationIdAccessor accessor, HttpRequestMessage request)
    {
        var inner = new CapturingInnerHandler();
        var handler = new CorrelationIdHandler(accessor) { InnerHandler = inner };
        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(request, CancellationToken.None);
        return inner.LastRequest;
    }

    [Fact]
    public async Task Adds_header_when_ambient_id_present()
    {
        var sent = await SendThrough(
            new StubAccessor { Current = "abc-123" },
            new HttpRequestMessage(HttpMethod.Get, "https://example.test/"));

        sent.Headers.TryGetValues(CorrelationConstants.HeaderName, out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be("abc-123");
    }

    [Fact]
    public async Task Omits_header_when_no_ambient_id()
    {
        var sent = await SendThrough(
            new StubAccessor { Current = null },
            new HttpRequestMessage(HttpMethod.Get, "https://example.test/"));

        sent.Headers.Contains(CorrelationConstants.HeaderName).Should().BeFalse();
    }

    [Fact]
    public async Task Does_not_overwrite_existing_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        request.Headers.TryAddWithoutValidation(CorrelationConstants.HeaderName, "explicit-id");

        var sent = await SendThrough(new StubAccessor { Current = "ambient-id" }, request);

        sent.Headers.GetValues(CorrelationConstants.HeaderName).Should().ContainSingle()
            .Which.Should().Be("explicit-id", "an explicitly-set correlation id must win over the ambient one");
    }

    [Fact]
    public void AddQuilt4NetCorrelationId_registers_accessor_and_handler()
    {
        var services = new ServiceCollection();
        services.AddQuilt4NetCorrelationId();
        var provider = services.BuildServiceProvider();

        provider.GetService<ICorrelationIdAccessor>().Should().NotBeNull("a fallback accessor must always resolve");
        provider.GetService<CorrelationIdHandler>().Should().NotBeNull();
    }

    [Fact]
    public void IHttpClientBuilder_opt_in_attaches_the_handler_to_that_client()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("internal-api").AddQuilt4NetCorrelationId();
        var provider = services.BuildServiceProvider();

        // The named client resolves and the handler is registered in its pipeline (no throw on create).
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("internal-api");
        client.Should().NotBeNull();
        provider.GetService<CorrelationIdHandler>().Should().NotBeNull();
    }

    [Fact]
    public void Default_accessor_without_http_host_yields_null_current()
    {
        // Base-package fallback: no IHttpContextAccessor / ASP.NET => nothing to propagate.
        var services = new ServiceCollection();
        services.AddQuilt4NetCorrelationId();
        var accessor = services.BuildServiceProvider().GetRequiredService<ICorrelationIdAccessor>();

        accessor.Current.Should().BeNull();
    }
}
