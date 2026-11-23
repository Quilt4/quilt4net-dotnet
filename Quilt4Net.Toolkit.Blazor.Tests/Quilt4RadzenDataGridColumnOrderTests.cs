using Bunit;
using AwesomeAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Radzen;
using Radzen.Blazor;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

/// <summary>
/// #162: <c>Quilt4RadzenDataGridColumn</c> used to <b>wrap</b> a <c>RadzenDataGridColumn</c> rather than be
/// one. A wrapper is a component in its own right, so the column it rendered registered with the grid a
/// generation later than a plain column declared beside it — and the grid orders columns by registration,
/// not by declaration. Mixing the two silently reordered the grid, whatever the markup said, with nothing
/// failing to point at it.
/// </summary>
public class Quilt4RadzenDataGridColumnOrderTests : BunitContext
{
    private readonly FakeContentService _content = new();
    private readonly FakeLanguageStateService _language = new();

    public Quilt4RadzenDataGridColumnOrderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IContentService>(_content);
        Services.AddSingleton<ILanguageStateService>(_language);
        Services.AddRadzenComponents();
        _language.Selected = new Language { Key = Guid.NewGuid(), Name = "English" };
    }

    [Fact]
    public void A_plain_column_declared_last_renders_last()
    {
        // The exact shape from the report: content-aware columns, then a plain action column at the end.
        _content.Map["col.org"] = "Organisation";
        _content.Map["col.feature"] = "Feature";

        var cut = RenderGrid(b =>
        {
            Quilt4Column(b, 0, "col.org", "Org", nameof(Row.Name));
            Quilt4Column(b, 10, "col.feature", "Feature", nameof(Row.Feature));
            PlainColumn(b, 20, "Actions");
        });

        Headers(cut).Should().Equal("Organisation", "Feature", "Actions");
    }

    [Fact]
    public void A_plain_column_declared_first_renders_first()
    {
        // The mirror case, so the test cannot pass by the wrapped columns simply always sorting later.
        _content.Map["col.org"] = "Organisation";

        var cut = RenderGrid(b =>
        {
            PlainColumn(b, 0, "Select");
            Quilt4Column(b, 10, "col.org", "Org", nameof(Row.Name));
        });

        Headers(cut).Should().Equal("Select", "Organisation");
    }

    [Fact]
    public void Declaration_order_holds_when_a_plain_column_sits_between_two_wrapped_ones()
    {
        _content.Map["col.org"] = "Organisation";
        _content.Map["col.feature"] = "Feature";

        var cut = RenderGrid(b =>
        {
            Quilt4Column(b, 0, "col.org", "Org", nameof(Row.Name));
            PlainColumn(b, 10, "Actions");
            Quilt4Column(b, 20, "col.feature", "Feature", nameof(Row.Feature));
        });

        Headers(cut).Should().Equal("Organisation", "Actions", "Feature");
    }

    [Fact]
    public void The_resolved_title_reaches_the_rendered_header()
    {
        // Inheriting moves the title onto the grid's own header rendering via SetTitle/GetTitle, so this
        // guards the path the wrapper used to own.
        _content.Map["col.org"] = "Organisation";

        var cut = RenderGrid(b => Quilt4Column(b, 0, "col.org", "Team", nameof(Row.Name)));

        Headers(cut).Should().Equal("Organisation");
    }

    [Fact]
    public void The_default_title_is_used_when_the_key_misses()
    {
        var cut = RenderGrid(b => Quilt4Column(b, 0, "col.absent", "Team", nameof(Row.Name)));

        Headers(cut).Should().Equal("Team");
    }

    [Fact]
    public void A_radzen_parameter_the_old_wrapper_never_forwarded_now_works()
    {
        // OrderIndex was one of the workarounds suggested in the report. Inheriting means it — and every
        // other Radzen column parameter — is simply available, rather than needing to be forwarded.
        _content.Map["col.org"] = "Organisation";
        _content.Map["col.feature"] = "Feature";

        var cut = RenderGrid(b =>
        {
            Quilt4Column(b, 0, "col.org", "Org", nameof(Row.Name), orderIndex: 1);
            Quilt4Column(b, 10, "col.feature", "Feature", nameof(Row.Feature), orderIndex: 0);
        });

        Headers(cut).Should().Equal("Feature", "Organisation");
    }

    private IRenderedComponent<RadzenDataGrid<Row>> RenderGrid(Action<RenderTreeBuilder> columns)
    {
        return Render<RadzenDataGrid<Row>>(p => p
            .Add(g => g.Data, new[] { new Row { Name = "a", Feature = "b" } })
            .Add(g => g.Columns, (RenderFragment)(b => columns(b))));
    }

    private static void Quilt4Column(RenderTreeBuilder b, int seq, string key, string @default, string property, int? orderIndex = null)
    {
        b.OpenComponent<Quilt4RadzenDataGridColumn<Row>>(seq);
        b.AddAttribute(seq + 1, nameof(Quilt4RadzenDataGridColumn<Row>.TitleKey), key);
        b.AddAttribute(seq + 2, nameof(Quilt4RadzenDataGridColumn<Row>.DefaultTitle), @default);
        b.AddAttribute(seq + 3, nameof(RadzenDataGridColumn<Row>.Property), property);
        if (orderIndex.HasValue)
        {
            b.AddAttribute(seq + 4, nameof(RadzenDataGridColumn<Row>.OrderIndex), orderIndex.Value);
        }
        b.CloseComponent();
    }

    private static void PlainColumn(RenderTreeBuilder b, int seq, string title)
    {
        b.OpenComponent<RadzenDataGridColumn<Row>>(seq);
        b.AddAttribute(seq + 1, nameof(RadzenDataGridColumn<Row>.Title), title);
        b.AddAttribute(seq + 2, nameof(RadzenDataGridColumn<Row>.Sortable), false);
        b.CloseComponent();
    }

    private static string[] Headers(IRenderedComponent<RadzenDataGrid<Row>> cut)
    {
        // .rz-column-title-content is the innermost node; matching .rz-column-title as well would count
        // each header twice, since Radzen nests the two.
        return cut.FindAll("thead th .rz-column-title-content")
            .Select(x => x.TextContent.Trim())
            .Where(x => x.Length > 0)
            .ToArray();
    }

    private class Row
    {
        public string Name { get; set; }
        public string Feature { get; set; }
    }

    private class FakeContentService : IContentService
    {
        public Dictionary<string, string> Map { get; } = new();

        public Task<(string Value, bool Success)> GetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null)
        {
            if (Map.TryGetValue(key ?? "", out var v) && !string.IsNullOrEmpty(v)) return Task.FromResult((v, true));
            return Task.FromResult((defaultValue ?? "", false));
        }

        public Task SetContentAsync(string key, string value, Guid languageKey, ContentFormat contentType, string application = null) => Task.CompletedTask;
        public Task ClearCacheAsync() => Task.CompletedTask;
    }

    private class FakeLanguageStateService : ILanguageStateService
    {
        public event EventHandler<LanguageLoadedEventArgs> LanguageLoadedEvent;
        public event EventHandler<LanguageChangedEventArgs> LanguageChangedEvent;
        public event EventHandler<DeveloperModeEventArgs> DeveloperModeEvent;
        public Language Selected { get; set; }
        public Language[] Languages { get; set; } = [];
        public bool DeveloperMode { get; set; }
        public Task<Language[]> ReloadAsync() => Task.FromResult(Languages);
        public void RaiseLanguageChanged() => LanguageChangedEvent?.Invoke(this, new LanguageChangedEventArgs());
        private void Unused() { LanguageLoadedEvent?.Invoke(this, null); DeveloperModeEvent?.Invoke(this, null); }
    }
}
