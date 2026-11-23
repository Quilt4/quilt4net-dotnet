using Bunit;
using AwesomeAssertions;
using Microsoft.AspNetCore.Components;
using Quilt4Net.Toolkit.Blazor.Framework;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

/// <summary>
/// A host that resolves its data in-process has no endpoint to probe, so it has no reason to
/// register an <see cref="IConnectionService"/>. <c>ConnectionWrapper</c> used to take that service
/// as a required injection, which Blazor resolves at render time whether or not the component uses
/// it — so "I already hold what I render" turned into a blank page and a circuit-killing
/// <c>InvalidOperationException</c>, with nothing on screen to say why.
/// </summary>
/// <remarks>
/// Note that no <see cref="IConnectionService"/> is registered anywhere in this fixture. That
/// absence is the point of every test here.
/// </remarks>
public class ConnectionWrapperUnregisteredTests : BunitContext
{
    public ConnectionWrapperUnregisteredTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Ready_renders_the_child_with_no_connection_service_registered()
    {
        var cut = Render<ConnectionWrapper>(p => p
            .Add(c => c.Service, Service.Issue)
            .Add(c => c.Ready, true)
            .Add(c => c.ChildContent, Child()));

        cut.Find("#figure").Should().NotBeNull();
    }

    [Fact]
    public void Not_ready_without_a_connection_service_names_the_missing_registration()
    {
        // Failing loudly is the whole point: the previous behaviour was a DI exception rendered as
        // nothing at all, which reads as "the feature is broken" rather than "the host is unwired".
        var cut = Render<ConnectionWrapper>(p => p
            .Add(c => c.Service, Service.Issue)
            .Add(c => c.Ready, false)
            .Add(c => c.ChildContent, Child()));

        cut.Markup.Should().Contain("IConnectionService");
        cut.Markup.Should().Contain("AddQuilt4NetIssues");
    }

    [Fact]
    public void A_configuration_failure_does_not_throw_while_rendering_its_own_message()
    {
        // ConnectionResult.Address is null for a configuration failure, and the "not connected"
        // branch dereferenced it unconditionally — throwing over the top of the message that would
        // have explained the problem.
        var cut = Render<ConnectionWrapper>(p => p
            .Add(c => c.Service, Service.Issue)
            .Add(c => c.Ready, false)
            .Add(c => c.ChildContent, Child()));

        cut.Markup.Should().Contain("Not connected to Quilt4Net");
        cut.FindAll("#figure").Should().BeEmpty("the child must not render when the wrapper could not verify anything");
    }

    private static RenderFragment Child() =>
        b => b.AddMarkupContent(0, "<span id=\"figure\">figure</span>");
}
