using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Quilt4Net.Toolkit.Api.Framework;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Api.Tests;

public class HttpContextCorrelationIdAccessorTests
{
    [Fact]
    public void Current_returns_the_id_stamped_by_the_middleware()
    {
        var ctx = new DefaultHttpContext();
        ctx.Items[CorrelationConstants.ItemKey] = "req-42";
        var accessor = new HttpContextCorrelationIdAccessor(new HttpContextAccessor { HttpContext = ctx });

        accessor.Current.Should().Be("req-42");
    }

    [Fact]
    public void Current_is_null_when_no_correlation_item()
    {
        var accessor = new HttpContextCorrelationIdAccessor(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        accessor.Current.Should().BeNull();
    }

    [Fact]
    public void Current_is_null_outside_a_request()
    {
        var accessor = new HttpContextCorrelationIdAccessor(new HttpContextAccessor { HttpContext = null });

        accessor.Current.Should().BeNull();
    }

    [Fact]
    public async Task Middleware_stores_id_under_the_shared_item_key()
    {
        // The accessor reads CorrelationConstants.ItemKey; the middleware must write the same key.
        // Guards against the two drifting apart.
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[CorrelationConstants.HeaderName] = "inbound-7";
        var middleware = new CorrelationIdMiddleware(
            _ => Task.CompletedTask,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(ctx);

        ctx.Items[CorrelationConstants.ItemKey].Should().Be("inbound-7");
        ctx.Response.Headers[CorrelationConstants.HeaderName].ToString().Should().Be("inbound-7");
    }
}
