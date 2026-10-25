using System.Net;
using System.Text.Json;
using FluentAssertions;
using Quilt4Net.Toolkit.Features.Issue;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Wire-shape tests for the issue tracker client: the URLs it calls, how it serializes, and how it
/// reports failure. The tracker client deliberately throws rather than degrading to a default, so
/// the failure assertions matter as much as the success ones.
/// </summary>
public class IssueServiceTests
{
    [Fact]
    public async Task GetAsync_calls_the_list_endpoint()
    {
        var handler = new RecordingHandler(_ => Json("[]"));
        var sut = CreateSut(handler);

        await sut.GetAsync(TestContext.Current.CancellationToken);

        handler.LastRequest.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/Api/Issue");
    }

    [Fact]
    public async Task GetAsync_by_number_calls_the_item_endpoint()
    {
        var handler = new RecordingHandler(_ => Json("null"));
        var sut = CreateSut(handler);

        await sut.GetAsync(12, TestContext.Current.CancellationToken);

        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/Api/Issue/12");
    }

    [Fact]
    public async Task GetAsync_by_number_returns_null_for_a_missing_issue()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = CreateSut(handler);

        var result = await sut.GetAsync(404, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRoadmapAsync_calls_the_roadmap_endpoint()
    {
        var handler = new RecordingHandler(_ => Json("""{"routes":[],"edges":[],"unroutedCount":0,"hiddenCount":0,"generatedUtc":"2026-09-03T00:00:00Z"}"""));
        var sut = CreateSut(handler);

        var roadmap = await sut.GetRoadmapAsync(TestContext.Current.CancellationToken);

        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/Api/Issue/roadmap");
        roadmap.Routes.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_calls_delete_and_tolerates_an_empty_body()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = CreateSut(handler);

        await sut.DeleteAsync(7, TestContext.Current.CancellationToken);

        handler.LastRequest.Method.Should().Be(HttpMethod.Delete);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/Api/Issue/7");
    }

    [Fact]
    public async Task RemoveLinkAsync_names_both_issues_in_the_url()
    {
        var handler = new RecordingHandler(_ => Json(SampleIssue));
        var sut = CreateSut(handler);

        await sut.RemoveLinkAsync(3, 9, TestContext.Current.CancellationToken);

        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/Api/Issue/3/link/9");
    }

    [Fact]
    public async Task Enums_are_sent_as_names_not_numbers()
    {
        string body = null;
        var handler = new RecordingHandler(r =>
        {
            body = r.Content!.ReadAsStringAsync().Result;
            return Json(SampleIssue);
        });
        var sut = CreateSut(handler);

        await sut.AddLinkAsync(1, new AddIssueLinkRequest
        {
            TargetNumber = 2,
            Kind = IssueLinkKind.Cheapens,
            Reason = "the schema settles first"
        }, TestContext.Current.CancellationToken);

        body.Should().Contain("Cheapens");
        body.Should().NotContain("\"kind\":1");
    }

    [Fact]
    public async Task A_response_without_a_plan_reads_as_no_plan()
    {
        // An older server sends no "plan" at all. That must read as an empty plan, not a failure.
        var sut = CreateSut(new RecordingHandler(_ => Json(SampleIssue)));

        var issue = await sut.GetAsync(1, TestContext.Current.CancellationToken);

        issue.Plan.Should().BeEmpty();
    }

    [Fact]
    public async Task The_plan_travels_both_ways()
    {
        string body = null;
        var handler = new RecordingHandler(r =>
        {
            body = r.Content!.ReadAsStringAsync().Result;
            return Json(SampleIssue.Replace("\"content\":\"\"", "\"content\":\"\",\"plan\":\"## Steps\\n- [ ] one\""));
        });
        var sut = CreateSut(handler);

        var issue = await sut.UpdateAsync(1, new UpdateIssueRequest { Title = "t", Plan = "## Steps\n- [ ] one" }, TestContext.Current.CancellationToken);

        body.Should().Contain("\"plan\":\"## Steps\\n- [ ] one\"");
        issue.Plan.Should().Be("## Steps\n- [ ] one");
    }

    [Fact]
    public async Task A_failure_throws_and_carries_the_status_code()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("Missing required scope 'issue:write'.")
        });
        var sut = CreateSut(handler);

        var act = () => sut.CreateAsync(new CreateIssueRequest { Title = "x" });

        var thrown = await act.Should().ThrowAsync<IssueServiceException>();
        thrown.Which.StatusCode.Should().Be(403);
        thrown.Which.Message.Should().Contain("issue:write");
    }

    [Theory]
    [InlineData("""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.4","title":"Forbidden","status":403,"detail":"'issue:read' belongs to Issues, which is switched off for this team."}""", "'issue:read' belongs to Issues, which is switched off for this team.")]
    [InlineData("""{"title":"Forbidden","status":403}""", "Forbidden")]
    [InlineData("""["not a problem"]""", """["not a problem"]""")]
    [InlineData("Missing required scope 'issue:write'.", "Missing required scope 'issue:write'.")]
    public async Task A_problem_response_is_reported_by_its_detail(string body, string expected)
    {
        var sut = CreateSut(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(body) }));

        var act = () => sut.GetRoadmapAsync();

        var thrown = await act.Should().ThrowAsync<IssueServiceException>();
        thrown.Which.Message.Should().Be($"Quilt4Net issue request failed with 403: {expected}");
    }

    [Fact]
    public async Task A_429_carries_the_servers_retry_after()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("slow down") };
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        var sut = CreateSut(new RecordingHandler(_ => response));

        var act = () => sut.GetAsync();

        var thrown = await act.Should().ThrowAsync<IssueServiceException>();
        thrown.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_failure_without_retry_after_leaves_it_null()
    {
        var sut = CreateSut(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var act = () => sut.GetAsync();

        var thrown = await act.Should().ThrowAsync<IssueServiceException>();
        thrown.Which.RetryAfter.Should().BeNull();
    }

    [Fact]
    public void Enum_names_are_the_wire_contract()
    {
        // Renaming a member is a breaking wire change, not a refactor, because the server stores and
        // compares these by name. Pin them so the rename shows up here rather than in production.
        Enum.GetNames<IssueLinkKind>().Should().BeEquivalentTo("Blocks", "Cheapens", "Overlaps");
        Enum.GetNames<RoadmapBand>().Should().BeEquivalentTo("Now", "Next", "Later");
        Enum.GetNames<IssueEffort>().Should().BeEquivalentTo("S", "M", "L");
        // These three are the backlog's own vocabulary. Renaming one silently decouples the tracker
        // from every backlog row that uses the old word.
        Enum.GetNames<IssueImportance>().Should().BeEquivalentTo("Critical", "Important", "Nice");
    }

    // Carries every field, because IssueResponse declares them all `required`: a payload missing one
    // fails to deserialize. That is the intended strictness, but it does mean adding a field to this
    // response is a wire break for any server still sending the older shape.
    private const string SampleIssue = """
        {"number":1,"title":"t","content":"","route":"","band":"Later","state":"Todo",
         "assignedUserKey":"","effort":null,"importance":null,"links":[],
         "createdUtc":"2026-09-03T00:00:00Z","updatedUtc":"2026-09-03T00:00:00Z"}
        """;

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    private static IssueService CreateSut(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler));

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://example.com/") };
    }
}
