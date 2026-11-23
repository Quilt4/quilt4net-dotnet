using AwesomeAssertions;
using Quilt4Net.Toolkit.Api;
using Xunit;

namespace Quilt4Net.Toolkit.Api.Tests;

public class SensitiveHeaderMaskingTests
{
    private static Request RequestWith(params (string Key, string Value)[] headers) => new()
    {
        Method = "GET",
        Path = "/api/x",
        Headers = headers.ToDictionary(h => h.Key, h => h.Value),
        Query = new Dictionary<string, string>(),
        Body = "",
        ClientIp = "127.0.0.1",
    };

    private static Response ResponseWith(params (string Key, string Value)[] headers) => new()
    {
        StatusCode = 200,
        Headers = headers.ToDictionary(h => h.Key, h => h.Value),
        Body = "",
    };

    private static async Task<(Request Req, Response Res)> RunDefault(LoggingOptions options, Request request, Response response)
    {
        var (req, res, _) = await options.Interceptor.Invoke(request, response, new Dictionary<string, string>(), null);
        return (req, res);
    }

    [Fact]
    public void Default_interceptor_is_the_header_masker()
    {
        new LoggingOptions().Interceptor.Should().NotBeNull("masking is the default; set it to null to log verbatim");
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("X-API-KEY")]
    [InlineData("Proxy-Authorization")]
    [InlineData("Cookie")]
    public async Task Default_masks_sensitive_request_header_values(string header)
    {
        var (req, _) = await RunDefault(new LoggingOptions(), RequestWith((header, "secret")), ResponseWith());

        req.Headers[header].Should().Be(LoggingOptions.HeaderMask);
    }

    [Fact]
    public async Task Default_masks_sensitive_response_header_values()
    {
        var (_, res) = await RunDefault(new LoggingOptions(), RequestWith(), ResponseWith(("Set-Cookie", "sid=abc")));

        res.Headers["Set-Cookie"].Should().Be(LoggingOptions.HeaderMask);
    }

    [Fact]
    public async Task Matching_is_case_insensitive()
    {
        var (req, _) = await RunDefault(new LoggingOptions(), RequestWith(("authorization", "Bearer abc"), ("x-api-key", "k")), ResponseWith());

        req.Headers["authorization"].Should().Be(LoggingOptions.HeaderMask);
        req.Headers["x-api-key"].Should().Be(LoggingOptions.HeaderMask);
    }

    [Fact]
    public async Task Non_sensitive_headers_pass_through_and_key_is_kept()
    {
        var (req, _) = await RunDefault(new LoggingOptions(),
            RequestWith(("Content-Type", "application/json"), ("Authorization", "Bearer abc")), ResponseWith());

        req.Headers["Content-Type"].Should().Be("application/json");
        req.Headers.Should().ContainKey("Authorization", "the key is retained; only the value is masked");
    }

    [Fact]
    public async Task Custom_sensitive_list_changes_what_is_masked()
    {
        var options = new LoggingOptions { SensitiveHeaders = ["X-Secret"] };
        var (req, _) = await RunDefault(options, RequestWith(("X-Secret", "shh"), ("Authorization", "Bearer abc")), ResponseWith());

        req.Headers["X-Secret"].Should().Be(LoggingOptions.HeaderMask);
        req.Headers["Authorization"].Should().Be("Bearer abc", "no longer in the sensitive list");
    }

    [Fact]
    public void Interceptor_can_be_disabled_for_verbatim_logging()
    {
        // The escape hatch: null interceptor => middleware logs request/response exactly as captured.
        var options = new LoggingOptions { Interceptor = null };
        options.Interceptor.Should().BeNull();
    }
}
