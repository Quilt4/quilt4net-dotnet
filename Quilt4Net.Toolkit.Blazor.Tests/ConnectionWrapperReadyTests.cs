using Bunit;
using AwesomeAssertions;
using Microsoft.AspNetCore.Components;
using Quilt4Net.Toolkit.Blazor.Framework;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

/// <summary>
/// #156: <c>ConnectionWrapper</c> put a connectivity probe in front of everything it wrapped, so the
/// language selector — whose menu is built entirely from state it already holds — spun on an HTTP
/// call to <c>Api/System/WhoAmI</c>, a request that answers a different question than "can this
/// render".
/// </summary>
public class ConnectionWrapperReadyTests : BunitContext
{
    private readonly SlowConnectionService _connectionService = new();

    public ConnectionWrapperReadyTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IConnectionService>(_connectionService);
    }

    [Fact]
    public void Ready_renders_the_child_without_waiting_for_the_probe()
    {
        var cut = Render<ConnectionWrapper>(p => p
            .Add(c => c.Service, Service.Content)
            .Add(c => c.Ready, true)
            .Add(c => c.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<span id=\"menu\">menu</span>"))));

        cut.Find("#menu").Should().NotBeNull();
    }

    [Fact]
    public void Ready_does_not_issue_the_probe_at_all()
    {
        Render<ConnectionWrapper>(p => p
            .Add(c => c.Service, Service.Content)
            .Add(c => c.Ready, true)
            .Add(c => c.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<span id=\"menu\">menu</span>"))));

        _connectionService.Probes.Should().Be(0);
    }

    [Fact]
    public void Not_ready_still_waits_on_the_probe()
    {
        // The probe is what distinguishes "nothing to show" from "not connected", so a component
        // with nothing of its own to render must still wait for it.
        var cut = Render<ConnectionWrapper>(p => p
            .Add(c => c.Service, Service.Content)
            .Add(c => c.Ready, false)
            .Add(c => c.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<span id=\"menu\">menu</span>"))));

        cut.FindAll("#menu").Should().BeEmpty();
        _connectionService.Probes.Should().Be(1);
    }

    [Fact]
    public void Becoming_ready_replaces_the_spinner_with_the_child()
    {
        // The language selector's real sequence: nothing to draw on the first render, then the
        // languages arrive and the menu must appear without the probe having answered.
        var cut = Render<ConnectionWrapper>(p => p
            .Add(c => c.Service, Service.Content)
            .Add(c => c.Ready, false)
            .Add(c => c.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<span id=\"menu\">menu</span>"))));

        cut.Render(p => p.Add(c => c.Ready, true));

        cut.Find("#menu").Should().NotBeNull();
    }

    /// <summary>Never completes, standing in for a probe still in flight.</summary>
    private sealed class SlowConnectionService : IConnectionService
    {
        public int Probes { get; private set; }

        public Task<ConnectionResult> CanConnectAsync(Service service)
        {
            Probes++;
            return new TaskCompletionSource<ConnectionResult>().Task;
        }
    }
}
