using Bunit;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quilt4Net.Toolkit.Blazor.Features.Content.Pages;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.Content.Pages;
using Radzen;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class ContentMenuTests : BunitContext
{
    private readonly StubReader _reader = new();
    private readonly StubHostEnvironment _hostEnvironment = new() { EnvironmentName = "Development" };

    public ContentMenuTests()
    {
        Services.AddSingleton<IContentPageReader>(_reader);
        Services.AddSingleton<ILanguageStateService>(new StubLanguageState());
        Services.AddSingleton<IHostEnvironment>(_hostEnvironment);
        // RadzenPanelMenu pulls these services from DI; without them the component throws during
        // render and the test never gets to assert on markup.
        Services.AddScoped<DialogService>();
        Services.AddScoped<NotificationService>();
        Services.AddScoped<ContextMenuService>();
        Services.AddScoped<TooltipService>();
    }

    [Fact]
    public void Renders_a_root_menu_item_for_each_top_level_page()
    {
        _reader.Items =
        [
            new ContentMenuItemDto { Id = "1", Slug = "home", Title = "Home", Order = 1, ShowInMenu = true },
            new ContentMenuItemDto { Id = "2", Slug = "about", Title = "About", Order = 2, ShowInMenu = true },
        ];

        var cut = Render<ContentMenu>();

        cut.Markup.Should().Contain("Home");
        cut.Markup.Should().Contain("About");
    }

    [Fact]
    public void Renders_root_items_in_Order_field_ascending()
    {
        // Authors set Order to reorder siblings — the menu must respect that even when the wire
        // delivers items in arbitrary order (server doesn't guarantee a sort).
        _reader.Items =
        [
            new ContentMenuItemDto { Id = "2", Slug = "second", Title = "Second", Order = 20, ShowInMenu = true },
            new ContentMenuItemDto { Id = "1", Slug = "first", Title = "First", Order = 10, ShowInMenu = true },
        ];

        var cut = Render<ContentMenu>();

        var firstIdx = cut.Markup.IndexOf("First", StringComparison.Ordinal);
        var secondIdx = cut.Markup.IndexOf("Second", StringComparison.Ordinal);
        firstIdx.Should().BeGreaterThanOrEqualTo(0);
        secondIdx.Should().BeGreaterThan(firstIdx);
    }

    [Fact]
    public void Filters_out_pages_with_ShowInMenu_false()
    {
        // Operators tick ShowInMenu off to hide draft / utility pages from navigation — the menu
        // must respect that without removing the page from the data set (it still has a URL).
        _reader.Items =
        [
            new ContentMenuItemDto { Id = "1", Slug = "home", Title = "Home", Order = 1, ShowInMenu = true },
            new ContentMenuItemDto { Id = "2", Slug = "draft", Title = "Hidden Draft", Order = 2, ShowInMenu = false },
        ];

        var cut = Render<ContentMenu>();

        cut.Markup.Should().Contain("Home");
        cut.Markup.Should().NotContain("Hidden Draft");
    }

    [Fact]
    public void Nests_children_under_their_parent_in_the_menu()
    {
        _reader.Items =
        [
            new ContentMenuItemDto { Id = "1", Slug = "docs", Title = "Documentation", Order = 1, ShowInMenu = true },
            new ContentMenuItemDto { Id = "2", Slug = "docs/intro", Title = "Intro", Order = 1, ShowInMenu = true, ParentPageId = "1" },
            new ContentMenuItemDto { Id = "3", Slug = "docs/api", Title = "API", Order = 2, ShowInMenu = true, ParentPageId = "1" },
        ];

        var cut = Render<ContentMenu>();

        cut.Markup.Should().Contain("Documentation");
        cut.Markup.Should().Contain("Intro");
        cut.Markup.Should().Contain("API");
    }

    [Fact]
    public void Treats_orphan_pages_whose_parent_is_hidden_as_roots()
    {
        // Edge case: the operator un-ticked ShowInMenu on the parent but left the children visible.
        // Don't hide the whole subtree silently — surface the children at the top level so a
        // partial change still produces usable navigation.
        _reader.Items =
        [
            new ContentMenuItemDto { Id = "1", Slug = "hidden-parent", Title = "Hidden Parent", Order = 1, ShowInMenu = false },
            new ContentMenuItemDto { Id = "2", Slug = "orphan-child", Title = "Orphan Child", Order = 1, ShowInMenu = true, ParentPageId = "1" },
        ];

        var cut = Render<ContentMenu>();

        cut.Markup.Should().NotContain("Hidden Parent");
        cut.Markup.Should().Contain("Orphan Child");
    }

    [Fact]
    public void Builds_hrefs_against_the_configured_RoutePrefix()
    {
        // Reader components mount under the consumer's chosen route (typically /content/{*slug}).
        // The menu has to respect that — bare /slug links would 404.
        _reader.Items =
        [
            new ContentMenuItemDto { Id = "1", Slug = "about", Title = "About", Order = 1, ShowInMenu = true },
        ];

        var cut = Render<ContentMenu>(p => p.Add(x => x.RoutePrefix, "/content/"));

        cut.Markup.Should().Contain("/content/about");
    }

    [Fact]
    public void Shows_empty_placeholder_when_reader_returns_an_empty_tree()
    {
        // No menu items registered => render a disabled placeholder so a developer can tell
        // "no data" apart from "component not rendering". Silent absence is the worst failure
        // mode — ApiKey missing, wrong address, or no ShowInMenu pages all look identical
        // otherwise.
        _reader.Items = [];

        var cut = Render<ContentMenu>();

        cut.Markup.Should().Contain("(no menu pages)");
    }

    [Fact]
    public void Suppresses_empty_placeholder_when_ShowEmptyPlaceholder_is_false()
    {
        // Production hosts that don't want the dev affordance can opt out — the menu then renders
        // an empty shell instead of the placeholder row.
        _reader.Items = [];

        var cut = Render<ContentMenu>(p => p.Add(x => x.ShowEmptyPlaceholder, false));

        cut.Markup.Should().NotContain("(no menu pages)");
    }

    [Fact]
    public void Hides_empty_placeholder_by_default_in_non_development_environments()
    {
        // Default is environment-aware: shown in Development (dev affordance), hidden in
        // Production (end users shouldn't see the dev hint).
        _hostEnvironment.EnvironmentName = "Production";
        _reader.Items = [];

        var cut = Render<ContentMenu>();

        cut.Markup.Should().NotContain("(no menu pages)");
    }

    [Fact]
    public void Forces_empty_placeholder_on_in_non_development_when_explicitly_true()
    {
        // Explicit override beats the env-aware default in both directions.
        _hostEnvironment.EnvironmentName = "Production";
        _reader.Items = [];

        var cut = Render<ContentMenu>(p => p.Add(x => x.ShowEmptyPlaceholder, true));

        cut.Markup.Should().Contain("(no menu pages)");
    }

    [Fact]
    public void Parent_with_children_is_rendered_without_a_Path_so_it_can_expand()
    {
        // Radzen quirk: RadzenPanelMenuItem with a Path is a navigation leaf — clicking always
        // navigates, never expands. So parents (items with children) must omit Path. Leaves keep
        // Path so they navigate. Verify by checking the rendered href set: only leaf slugs appear.
        _reader.Items =
        [
            new ContentMenuItemDto { Id = "1", Slug = "docs", Title = "Documentation", Order = 1, ShowInMenu = true },
            new ContentMenuItemDto { Id = "2", Slug = "docs/intro", Title = "Intro", Order = 1, ShowInMenu = true, ParentPageId = "1" },
        ];

        var cut = Render<ContentMenu>();

        cut.Markup.Should().NotContain("href=\"/docs\"");       // parent: no Path
        cut.Markup.Should().NotContain("href=\"docs\"");        // (defensive against href shape)
        cut.Markup.Should().Contain("docs/intro");              // leaf: navigable
    }

    private sealed class StubReader : IContentPageReader
    {
        public IReadOnlyList<ContentMenuItemDto> Items { get; set; } = Array.Empty<ContentMenuItemDto>();

        public Task<ContentPageDto> GetBySlugAsync(string slug, Guid languageKey, string application = null)
            => Task.FromResult<ContentPageDto>(null);

        public Task<IReadOnlyList<ContentMenuItemDto>> GetTreeAsync(Guid languageKey, string application = null)
            => Task.FromResult(Items);
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class StubLanguageState : ILanguageStateService
    {
        public event EventHandler<LanguageLoadedEventArgs> LanguageLoadedEvent;
        public event EventHandler<LanguageChangedEventArgs> LanguageChangedEvent;
        public event EventHandler<DeveloperModeEventArgs> DeveloperModeEvent;

        public Language Selected { get; set; } = new() { Name = "Default", Key = Guid.Empty };
        public Language[] Languages { get; set; } = [];
        public bool DeveloperMode { get; set; }

        public Task<Language[]> ReloadAsync() => Task.FromResult(Languages);

        private void Unused() { LanguageLoadedEvent?.Invoke(this, null); LanguageChangedEvent?.Invoke(this, null); DeveloperModeEvent?.Invoke(this, null); }
    }
}
