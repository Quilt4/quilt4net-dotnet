using System.Text.Json;
using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class StackFrameParserTests
{
    private const string SampleDetails = @"
[
  {
    ""parsedStack"": [
      { ""level"": 0, ""method"": ""Quilt4Net.Server.Features.Team.UserService.GetAsync"", ""assembly"": ""Quilt4Net.Server"", ""fileName"": ""D:\\a\\1\\s\\Quilt4Net.Server\\Features\\Team\\UserService.cs"", ""line"": 24 },
      { ""level"": 1, ""method"": ""System.Threading.Tasks.Task.Run"", ""assembly"": ""System.Private.CoreLib"" }
    ]
  }
]";

    [Fact]
    public void Parse_returns_empty_when_raw_or_details_is_null()
    {
        StackFrameParser.Parse(null).Should().BeEmpty();
        StackFrameParser.Parse(new Dictionary<string, object>()).Should().BeEmpty();
        StackFrameParser.Parse(new Dictionary<string, object> { ["Details"] = null }).Should().BeEmpty();
    }

    [Fact]
    public void Parse_handles_string_payload_and_extracts_frames()
    {
        var raw = new Dictionary<string, object> { ["Details"] = SampleDetails };

        var frames = StackFrameParser.Parse(raw);

        frames.Should().HaveCount(2);
        frames[0].Level.Should().Be(0);
        frames[0].Method.Should().Be("Quilt4Net.Server.Features.Team.UserService.GetAsync");
        frames[0].Assembly.Should().Be("Quilt4Net.Server");
        frames[0].FileName.Should().Contain("UserService.cs");
        frames[0].Line.Should().Be(24);
        frames[0].HasFileLocation.Should().BeTrue();
        frames[1].HasFileLocation.Should().BeFalse(); // missing fileName + line
    }

    [Fact]
    public void Parse_handles_pre_deserialized_JsonElement()
    {
        using var doc = JsonDocument.Parse(SampleDetails);
        var raw = new Dictionary<string, object> { ["Details"] = doc.RootElement };

        var frames = StackFrameParser.Parse(raw);

        frames.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_returns_empty_on_malformed_json_string()
    {
        var raw = new Dictionary<string, object> { ["Details"] = "{ this is not json" };

        StackFrameParser.Parse(raw).Should().BeEmpty();
    }

    [Fact]
    public void ToResharperPath_strips_path_up_to_and_including_a_known_root()
    {
        var frame = new StackFrame
        {
            FileName = @"D:\a\1\s\Quilt4Net.Server\Features\Team\UserService.cs",
            Line = 24
        };

        var result = StackFrameParser.ToResharperPath(frame, ["Quilt4Net.Server"]);

        result.Should().Be(@"\Features\Team\UserService.cs:line 24");
    }

    [Fact]
    public void ToResharperPath_works_with_forward_slashes()
    {
        var frame = new StackFrame
        {
            FileName = "/home/runner/work/quilt4net/quilt4net/Quilt4Net.Server/Features/Team/UserService.cs",
            Line = 24
        };

        var result = StackFrameParser.ToResharperPath(frame, ["Quilt4Net.Server"]);

        result.Should().Be(@"\Features\Team\UserService.cs:line 24");
    }

    [Fact]
    public void ToResharperPath_returns_full_path_when_no_root_matches()
    {
        var frame = new StackFrame
        {
            FileName = @"C:\code\OtherProject\Foo.cs",
            Line = 5
        };

        var result = StackFrameParser.ToResharperPath(frame, ["Quilt4Net.Server"]);

        result.Should().Be(@"C:\code\OtherProject\Foo.cs:line 5");
    }

    [Fact]
    public void ToResharperPath_omits_line_suffix_when_line_is_zero()
    {
        var frame = new StackFrame
        {
            FileName = @"D:\a\1\s\Quilt4Net.Server\Foo.cs",
            Line = 0
        };

        var result = StackFrameParser.ToResharperPath(frame, ["Quilt4Net.Server"]);

        result.Should().Be(@"\Foo.cs");
    }

    [Fact]
    public void ToResharperPath_returns_empty_for_a_frame_without_a_filename()
    {
        var frame = new StackFrame { FileName = string.Empty, Line = 24 };

        StackFrameParser.ToResharperPath(frame, ["Quilt4Net.Server"]).Should().BeEmpty();
    }

    [Fact]
    public void ToResharperPath_uses_the_rightmost_matching_root_when_path_contains_a_collision()
    {
        // Pathological: root name appears twice (e.g., a folder structure where the project
        // name is also the name of an inner folder). Expect the rightmost match wins so the
        // shortest sensible relative path comes out.
        var frame = new StackFrame
        {
            FileName = @"C:\repos\Quilt4Net.Server\backup\Quilt4Net.Server\Features\Foo.cs",
            Line = 1
        };

        var result = StackFrameParser.ToResharperPath(frame, ["Quilt4Net.Server"]);

        result.Should().Be(@"\Features\Foo.cs:line 1");
    }
}
