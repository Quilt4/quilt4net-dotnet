using FluentAssertions;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

public class RequestBatcherTests
{
    [Fact]
    public async Task Requests_made_within_the_window_go_out_as_one_batch()
    {
        var batches = new List<IReadOnlyList<int>>();
        var batcher = new RequestBatcher<int, string>(r => { lock (batches) batches.Add(r); return Task.FromResult<IReadOnlyList<string>>(r.Select(x => $"v{x}").ToArray()); },
            TimeSpan.FromMilliseconds(50), maxSize: 100);

        var results = await Task.WhenAll(Enumerable.Range(1, 5).Select(batcher.EnqueueAsync));

        results.Should().Equal("v1", "v2", "v3", "v4", "v5");
        batches.Should().ContainSingle().Which.Should().HaveCount(5);
    }

    [Fact]
    public async Task A_full_batch_goes_at_once_without_waiting_for_the_window()
    {
        var batches = new List<IReadOnlyList<int>>();
        var batcher = new RequestBatcher<int, int>(r => { lock (batches) batches.Add(r); return Task.FromResult<IReadOnlyList<int>>(r.ToArray()); },
            TimeSpan.FromSeconds(30), maxSize: 3);

        var results = await Task.WhenAll(Enumerable.Range(1, 3).Select(batcher.EnqueueAsync)).WaitAsync(TimeSpan.FromSeconds(5));

        results.Should().Equal(1, 2, 3);
        batches.Should().ContainSingle();
    }

    [Fact]
    public async Task A_failed_batch_fails_every_caller_in_it()
    {
        var batcher = new RequestBatcher<int, int>(_ => throw new HttpRequestException("down"), TimeSpan.FromMilliseconds(20), maxSize: 100);

        var calls = Enumerable.Range(1, 3).Select(batcher.EnqueueAsync).ToArray();

        foreach (var call in calls)
            await call.Invoking(c => c).Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task An_answer_of_the_wrong_length_is_a_failure_not_a_mismatch()
    {
        // Matching by position is only safe when the counts agree; a short answer must not hand
        // one key another key's value.
        var batcher = new RequestBatcher<int, int>(_ => Task.FromResult<IReadOnlyList<int>>([1]), TimeSpan.FromMilliseconds(20), maxSize: 100);

        var calls = new[] { batcher.EnqueueAsync(1), batcher.EnqueueAsync(2) };

        foreach (var call in calls)
            await call.Invoking(c => c).Should().ThrowAsync<InvalidOperationException>();
    }
}
