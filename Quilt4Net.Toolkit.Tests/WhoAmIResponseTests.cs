using System.Text.Json;
using FluentAssertions;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class WhoAmIResponseTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Deserialize_Full_Response()
    {
        var json = """
        {
            "teamKey": "team-123",
            "teamName": "My Team",
            "accessLevel": "User",
            "scopes": ["content:read", "content:write", "config:read"]
        }
        """;

        var response = JsonSerializer.Deserialize<WhoAmIResponse>(json, JsonOptions);

        response.TeamKey.Should().Be("team-123");
        response.TeamName.Should().Be("My Team");
        response.AccessLevel.Should().Be("User");
        response.Scopes.Should().BeEquivalentTo("content:read", "content:write", "config:read");
    }

    [Fact]
    public void Deserialize_Legacy_Response_Without_Scopes()
    {
        var json = """
        {
            "key": "team-123",
            "name": "My Team"
        }
        """;

        var response = JsonSerializer.Deserialize<WhoAmIResponse>(json, JsonOptions);

        response.TeamKey.Should().BeNull();
        response.TeamName.Should().BeNull();
        response.AccessLevel.Should().BeNull();
        response.Scopes.Should().BeEmpty();
    }

    [Fact]
    public void HasScope_Returns_True_When_Present()
    {
        var response = new WhoAmIResponse
        {
            TeamKey = "team-123",
            TeamName = "My Team",
            AccessLevel = "User",
            Scopes = ["content:read", "content:write", "config:read"]
        };

        response.HasScope("content:read").Should().BeTrue();
        response.HasScope("content:write").Should().BeTrue();
        response.HasScope("config:read").Should().BeTrue();
    }

    [Fact]
    public void HasScope_Returns_False_When_Missing()
    {
        var response = new WhoAmIResponse
        {
            TeamKey = "team-123",
            TeamName = "My Team",
            AccessLevel = "Viewer",
            Scopes = ["content:read", "config:read"]
        };

        response.HasScope("content:write").Should().BeFalse();
        response.HasScope("config:write").Should().BeFalse();
        response.HasScope("language:read").Should().BeFalse();
    }

    [Fact]
    public void HasScope_Returns_False_On_Empty_Scopes()
    {
        var response = new WhoAmIResponse
        {
            TeamKey = "team-123",
            TeamName = "My Team"
        };

        response.HasScope("content:read").Should().BeFalse();
    }

    [Fact]
    public void ConnectionResult_With_Null_Capabilities_Defaults_Gracefully()
    {
        var result = new ConnectionResult
        {
            Success = true,
            Message = "OK",
            Address = new Uri("https://localhost")
        };

        result.Capabilities.Should().BeNull();
        (result.Capabilities?.HasScope("content:read") ?? true).Should().BeTrue();
    }

    [Fact]
    public void An_area_is_on_off_or_not_known_when_the_server_does_not_say()
    {
        var response = new WhoAmIResponse { Areas = ["Content", "Monitoring"] };

        response.IsAreaOn("monitoring").Should().BeTrue();
        response.IsAreaOn("Issues").Should().BeFalse();
        new WhoAmIResponse().IsAreaOn("Content").Should().BeNull("an older server does not say, which is not the same as off");
    }
}
