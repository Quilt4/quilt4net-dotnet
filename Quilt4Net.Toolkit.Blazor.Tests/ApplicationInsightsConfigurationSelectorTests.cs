using AwesomeAssertions;
using Quilt4Net.Toolkit.Blazor.Features.ApplicationInsights;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

#pragma warning disable xUnit1051

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class ApplicationInsightsConfigurationSelectorTests
{
    [Fact]
    public async Task LoadAsync_Empty_List_Marks_Loaded_With_Null_Selected()
    {
        var provider = new FakeProvider([]);
        var selector = new ApplicationInsightsConfigurationSelector(provider);

        await selector.LoadAsync();

        selector.IsLoaded.Should().BeTrue();
        selector.Available.Should().BeEmpty();
        selector.Selected.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_Defaults_To_First_When_Multiple()
    {
        var provider = new FakeProvider([
            new() { Id = "a", Name = "Prod" },
            new() { Id = "b", Name = "Test" }
        ]);
        var selector = new ApplicationInsightsConfigurationSelector(provider);

        await selector.LoadAsync();

        selector.Selected!.Id.Should().Be("a");
    }

    [Fact]
    public async Task SelectAsync_Switches_Selected_And_Raises_OnChanged()
    {
        var provider = new FakeProvider([
            new() { Id = "a", Name = "Prod" },
            new() { Id = "b", Name = "Test" }
        ]);
        var selector = new ApplicationInsightsConfigurationSelector(provider);
        await selector.LoadAsync();

        var notifications = 0;
        selector.OnChanged += () => notifications++;

        await selector.SelectAsync("b");

        selector.Selected!.Id.Should().Be("b");
        notifications.Should().Be(1);
    }

    [Fact]
    public async Task SelectAsync_Unknown_Id_Is_NoOp()
    {
        var provider = new FakeProvider([new() { Id = "a", Name = "Prod" }]);
        var selector = new ApplicationInsightsConfigurationSelector(provider);
        await selector.LoadAsync();

        var notifications = 0;
        selector.OnChanged += () => notifications++;

        await selector.SelectAsync("does-not-exist");

        selector.Selected!.Id.Should().Be("a");
        notifications.Should().Be(0);
    }

    [Fact]
    public async Task LoadAsync_Is_Idempotent()
    {
        var provider = new FakeProvider([new() { Id = "a", Name = "Prod" }]);
        var selector = new ApplicationInsightsConfigurationSelector(provider);

        await selector.LoadAsync();
        var firstHits = provider.HitCount;
        await selector.LoadAsync();

        provider.HitCount.Should().Be(firstHits, "load-once per circuit; reloads must not refetch");
    }

    private sealed class FakeProvider : IApplicationInsightsConfigurationProvider
    {
        private readonly ApplicationInsightsConfigurationResponse[] _items;
        public int HitCount { get; private set; }

        public FakeProvider(ApplicationInsightsConfigurationResponse[] items)
        {
            _items = items;
        }

        public Task<ApplicationInsightsConfigurationResponse[]> GetAllAsync(CancellationToken cancellationToken = default)
        {
            HitCount++;
            return Task.FromResult(_items);
        }
    }
}
