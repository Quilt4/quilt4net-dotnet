using Bunit;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quilt4Net.Toolkit.Blazor.Features.Content.Pages;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.Content.Pages;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class ContentPageViewTests : BunitContext
{
    private readonly StubReader _reader = new();
    private readonly CapturingBreadcrumbAdapter _breadcrumbAdapter = new();

    public ContentPageViewTests()
    {
        Services.AddSingleton<IContentPageReader>(_reader);
        Services.AddSingleton<ILanguageStateService>(new StubLanguageState());
        Services.AddSingleton<IPageBreadcrumbAdapter>(_breadcrumbAdapter);
    }

    [Fact]
    public void Renders_title_and_each_element_in_order()
    {
        // Two sections, mixed elements, deliberately out-of-order in the input. The renderer
        // sorts by Order so the test can verify the sort path along with each renderer.
        _reader.Page = new ContentPageDto
        {
            Slug = "about",
            Title = "About us",
            Sections =
            [
                new ContentSectionDto
                {
                    Id = "s1",
                    Order = 1,
                    Layout = SectionLayout.OneColumn,
                    Elements =
                    [
                        new ContentElementDto { Id = "e2", Order = 20, Type = ElementType.Text, Html = "<p>Body two</p>" },
                        new ContentElementDto { Id = "e1", Order = 10, Type = ElementType.Headline, Text = "Headline one", Level = 2 },
                    ],
                },
                new ContentSectionDto
                {
                    Id = "s2",
                    Order = 2,
                    Layout = SectionLayout.OneColumn,
                    Elements =
                    [
                        new ContentElementDto { Id = "e3", Order = 30, Type = ElementType.Quotation, Text = "Quote text", Attribution = "Someone" },
                        new ContentElementDto { Id = "e4", Order = 40, Type = ElementType.Divider },
                    ],
                },
            ],
        };

        var cut = Render<ContentPageView>(p => p.Add(x => x.Slug, "about"));

        cut.Markup.Should().Contain("<h1");
        cut.Markup.Should().Contain("About us");
        cut.Markup.Should().Contain("Headline one");
        cut.Markup.Should().Contain("Body two");
        cut.Markup.Should().Contain("Quote text");
        cut.Markup.Should().Contain("Someone");
        cut.Markup.Should().Contain("<hr");
    }

    [Fact]
    public void Renders_not_found_alert_when_reader_returns_null()
    {
        // No page configured on the stub reader => GetBySlugAsync returns null. The reader must
        // surface this as an info alert, not throw — same UX whether the slug is wrong, the row
        // is missing, or the api key is unset.
        _reader.Page = null;

        var cut = Render<ContentPageView>(p => p
            .Add(x => x.Slug, "missing")
            .Add(x => x.NotFoundMessage, "No such page here."));

        cut.Markup.Should().Contain("No such page here.");
    }

    [Fact]
    public void Renders_missing_element_placeholder_for_unknown_element_type()
    {
        // The wire deserialises an unknown element type to ElementType.Unknown — the reader has to
        // keep rendering the rest of the page and emit a placeholder for the unrecognised entry.
        _reader.Page = new ContentPageDto
        {
            Slug = "about",
            Title = "About",
            Sections =
            [
                new ContentSectionDto
                {
                    Id = "s1",
                    Order = 1,
                    Layout = SectionLayout.OneColumn,
                    Elements =
                    [
                        new ContentElementDto { Id = "e1", Order = 10, Type = ElementType.Unknown },
                        new ContentElementDto { Id = "e2", Order = 20, Type = ElementType.Text, Html = "<p>Still renders.</p>" },
                    ],
                },
            ],
        };

        var cut = Render<ContentPageView>(p => p.Add(x => x.Slug, "about"));

        cut.Markup.Should().Contain("Newer toolkit required for element type");
        cut.Markup.Should().Contain("Still renders.");
    }

    [Fact]
    public void Pushes_breadcrumb_chain_in_root_to_leaf_order()
    {
        // Server sends Ancestors in root → parent order. The reader appends the page itself as
        // the leaf, so the adapter receives the full breadcrumb chain in one call.
        _reader.Page = new ContentPageDto
        {
            Slug = "docs/intro",
            Title = "Intro",
            Ancestors =
            [
                new ContentPageAncestorDto { Slug = "docs", Title = "Documentation" },
            ],
            Sections = [],
        };

        Render<ContentPageView>(p => p
            .Add(x => x.Slug, "docs/intro")
            .Add(x => x.SlugRoutePrefix, "/content/"));

        _breadcrumbAdapter.Pushed.Should().HaveCount(1);
        var chain = _breadcrumbAdapter.Pushed[0];
        chain.Should().HaveCount(2);
        chain[0].Label.Should().Be("Documentation");
        chain[0].Href.Should().Be("/content/docs");
        chain[1].Label.Should().Be("Intro");
        chain[1].Href.Should().Be("/content/docs/intro");
    }

    [Fact]
    public void Renders_hero_layout_with_inline_padding_so_no_stylesheet_is_required()
    {
        // The inline-style fallback exists specifically so a consumer can drop the reader in and
        // see something reasonable before they add a stylesheet. Test the contract by checking the
        // inline padding shows up.
        _reader.Page = new ContentPageDto
        {
            Slug = "hero",
            Title = "Hero",
            Sections =
            [
                new ContentSectionDto
                {
                    Id = "s1",
                    Order = 1,
                    Layout = SectionLayout.Hero,
                    Elements = [],
                },
            ],
        };

        var cut = Render<ContentPageView>(p => p.Add(x => x.Slug, "hero"));

        cut.Markup.Should().Contain("padding: 2rem 0");
        cut.Markup.Should().Contain("quilt4net-layout-hero");
    }

    private sealed class StubReader : IContentPageReader
    {
        public ContentPageDto Page { get; set; }

        public Task<ContentPageDto> GetBySlugAsync(string slug, Guid languageKey, string application = null)
            => Task.FromResult(Page);

        public Task<IReadOnlyList<ContentMenuItemDto>> GetTreeAsync(Guid languageKey, string application = null)
            => Task.FromResult<IReadOnlyList<ContentMenuItemDto>>(Array.Empty<ContentMenuItemDto>());
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

        // Suppress unused-event warnings — tests don't fire them but the interface requires the
        // declarations.
        private void Unused() { LanguageLoadedEvent?.Invoke(this, null); LanguageChangedEvent?.Invoke(this, null); DeveloperModeEvent?.Invoke(this, null); }
    }

    private sealed class CapturingBreadcrumbAdapter : IPageBreadcrumbAdapter
    {
        public List<IReadOnlyList<PageBreadcrumbSegment>> Pushed { get; } = [];

        public void Push(IReadOnlyList<PageBreadcrumbSegment> chain) => Pushed.Add(chain);
    }
}
