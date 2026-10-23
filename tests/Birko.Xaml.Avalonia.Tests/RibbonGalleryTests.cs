using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Core.Ribbon;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// TASK-761 (FEATURE-019 D11 on the desktop): galleries, as b-ribbon has had since TASK-740. A row of tiles in a framed,
/// open or dropdown look; picking selects, runs and reports with the gallery as parent; ▲ ▼ step a row with disabled
/// ends; the full grid by arrows, Enter and Escape; two tiles in a small group; the grid over a folded group's popup;
/// the narrow menu; key tips. The redraw is in place, so stepping inside an unpinned reveal keeps it open.
/// </summary>
public class RibbonGalleryTests
{
    private static readonly List<string> Ran = new();

    private static RibbonGallery Styles(RibbonGalleryStyle? style = null) => new()
    {
        Selected = "normal",
        Columns = 3,
        Style = style,
        Items = new[] { "Normal", "Heading 1", "Heading 2", "Title", "Quote", "Code" }
            .Select(l => new RibbonGalleryItem
            {
                Id = l.ToLowerInvariant().Replace(" ", ""), Label = l, Preview = l == "Normal" ? "Aa" : null,
                Keytip = l == "Quote" ? "Q" : null, Run = () => Ran.Add(l),
            }).ToList(),
    };

    private static RibbonTab Home(RibbonGalleryStyle? style = null) => new()
    {
        Id = "home", Label = "Home", Groups = new[]
        {
            new RibbonGroup { Label = "Clipboard", Icon = "📋", Items = new[] { new RibbonItem { Id = "paste", Label = "Paste", Icon = "📋" } } },
            new RibbonGroup { Label = "Styles", Icon = "🅰", Items = new[] { new RibbonItem { Id = "styles", Label = "Styles", Gallery = Styles(style) } } },
        },
    };

    private static Ribbon Build(RibbonGroupSize size = RibbonGroupSize.Medium, RibbonGalleryStyle? style = null) => new()
    {
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        NarrowThreshold = 0,
        PreferredGroupSize = size,
        Tabs = new[] { Home(style) },
    };

    private static Ribbon Show(Ribbon ribbon, out Window window, double width = 1000)
    {
        window = new Window { Content = ribbon, Width = width, Height = 300 };
        window.Show();
        Settle(window, width);
        return ribbon;
    }

    private static void Settle(Window window, double width = 1000)
    {
        window.Measure(new Size(width, 300));
        window.Arrange(new Rect(0, 0, width, 300));
        for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
    }

    private static string? Name(Control c) => global::Avalonia.Automation.AutomationProperties.GetName(c);

    private static Panel PanelOf(Ribbon ribbon) =>
        (Panel)ribbon.GetVisualDescendants().First(v => v.GetType().Name == "RibbonGroupsPanel");

    /// <summary>The shown copies only: the scaling panel parks the other sizes off-screen.</summary>
    private static List<Button> Shown(Visual scope) =>
        scope.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsVisible && b.TranslatePoint(default, scope) is { X: > -1000 }).ToList();

    private static List<string?> RowTiles(Visual scope) =>
        Shown(scope).Where(b => b.Tag is string t && t.StartsWith("tile:")).Select(Name).ToList();

    private static Button Named(Visual scope, string name) => Shown(scope).First(b => Name(b) == name);

    private static void Press(Button b, Window window, double width = 1000)
    {
        ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(b)).Invoke();
        Settle(window, width);
    }

    private static Flyout? Grid(Ribbon ribbon) => (Flyout?)typeof(Ribbon).GetProperty("OpenGalleryGridFlyout",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ribbon);

    private static List<Button> GridTiles(Ribbon ribbon) =>
        ((Control)Grid(ribbon)!.Content!).GetVisualDescendants().OfType<Button>().ToList();

    private static void Key(Window window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        window.KeyRelease(key, RawInputModifiers.None, physical, null);
        Settle(window);
    }

    [AvaloniaFact]
    public void The_framed_row_shows_four_tiles_and_steps_a_row_at_a_time_with_disabled_ends()
    {
        var ribbon = Show(Build(), out var window);
        var panel = PanelOf(ribbon);
        RowTiles(panel).Should().Equal("Normal", "Heading 1", "Heading 2", "Title");
        Named(panel, "Show all: Styles").Should().NotBeNull();
        ribbon.GetVisualDescendants().OfType<Button>().First(b => Name(b) == "Previous choices" && b.TranslatePoint(default, panel) is { X: > -1000 })
            .IsEnabled.Should().BeFalse("nothing before the first row");

        Press(Named(panel, "More choices"), window);
        RowTiles(PanelOf(ribbon)).Should().Equal(new[] { "Heading 2", "Title", "Quote", "Code" }, "the last row is a full one, as the web's");
        ribbon.GetVisualDescendants().OfType<Button>().Where(b => Name(b) == "More choices" && b.TranslatePoint(default, PanelOf(ribbon)) is { X: > -1000 })
            .Should().OnlyContain(b => !b.IsEnabled, "nothing after the last row");
        Press(Named(PanelOf(ribbon), "Previous choices"), window);
        RowTiles(PanelOf(ribbon)).Should().StartWith(new[] { "Normal" });
    }

    [AvaloniaFact]
    public void Picking_a_tile_selects_runs_and_reports_it_with_the_gallery_as_parent()
    {
        Ran.Clear();
        var ribbon = Show(Build(), out var window);
        var picks = new List<RibbonMenuItemInvokedEventArgs>();
        ribbon.GalleryItemPicked += (_, e) => picks.Add(e);
        var normal = Named(PanelOf(ribbon), "Normal");
        ((ISelectionItemProvider)ControlAutomationPeer.CreatePeerForElement(normal)).IsSelected.Should().BeTrue("the declared current choice");

        Press(Named(PanelOf(ribbon), "Title"), window);
        Ran.Should().Equal("Title");
        picks.Should().ContainSingle().Which.Should().BeEquivalentTo(new { TabId = "home", ItemId = "title", ParentId = "styles" });
        ribbon.GetGallerySelection("styles").Should().Be("title");
        var title = Named(PanelOf(ribbon), "Title");
        ((ISelectionItemProvider)ControlAutomationPeer.CreatePeerForElement(title)).IsSelected.Should().BeTrue();
        ((ISelectionItemProvider)ControlAutomationPeer.CreatePeerForElement(Named(PanelOf(ribbon), "Normal"))).IsSelected.Should().BeFalse();
        ControlAutomationPeer.CreatePeerForElement(title).GetAutomationControlType().Should().Be(AutomationControlType.RadioButton);
    }

    [AvaloniaFact]
    public void The_app_sets_and_clears_the_selection_without_an_event()
    {
        var ribbon = Show(Build(), out var window);
        int picks = 0;
        ribbon.GalleryItemPicked += (_, _) => picks++;
        ribbon.SetGallerySelection("styles", "heading2");
        Settle(window);
        ribbon.GetGallerySelection("styles").Should().Be("heading2");
        ((ISelectionItemProvider)ControlAutomationPeer.CreatePeerForElement(Named(PanelOf(ribbon), "Heading 2"))).IsSelected.Should().BeTrue();
        ribbon.SetGallerySelection("styles", null);
        ribbon.GetGallerySelection("styles").Should().BeNull();
        picks.Should().Be(0);
    }

    [AvaloniaFact]
    public void The_selected_tile_has_the_pressed_look_in_tokens()
    {
        var ribbon = Show(Build(), out _);
        var normal = Named(PanelOf(ribbon), "Normal");
        var frame = (Border)normal.Content!;
        Color Res(string key) => normal.TryFindResource(key, normal.ActualThemeVariant, out var v) && v is ISolidColorBrush b ? b.Color : default;
        ((ISolidColorBrush)frame.BorderBrush!).Color.Should().Be(Res("BColorPrimaryBrush"));
        ((ISolidColorBrush)frame.Background!).Color.Should().Be(Res("BColorPrimaryLightBrush"));
        frame.Bounds.Width.Should().Be(64, "BRibbonGalleryTileWidth");
        var other = (Border)Named(PanelOf(ribbon), "Title").Content!;
        other.Bounds.Size.Should().Be(frame.Bounds.Size, "picking never resizes a tile");
    }

    [AvaloniaFact]
    public void The_open_look_has_arrows_around_the_row_and_More_under_it()
    {
        var ribbon = Show(Build(style: RibbonGalleryStyle.Open), out _);
        var panel = PanelOf(ribbon);
        var prev = Named(panel, "Previous choices").TranslatePoint(default, panel)!.Value;
        var first = Named(panel, "Normal").TranslatePoint(default, panel)!.Value;
        var more = Named(panel, "Show all: Styles");
        prev.X.Should().BeLessThan(first.X, "‹ before the row");
        more.TranslatePoint(default, panel)!.Value.Y.Should().BeGreaterThan(first.Y, "More ▾ under it");
        more.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain("More ▾");
    }

    [AvaloniaFact]
    public void The_dropdown_look_shows_only_the_current_choice()
    {
        var ribbon = Build();
        ribbon.GalleryStyle = RibbonGalleryStyle.Dropdown;
        Show(ribbon, out var window);
        RowTiles(PanelOf(ribbon)).Should().BeEmpty();
        var current = Named(PanelOf(ribbon), "Show all: Styles");
        current.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain(new[] { "Aa", "Normal ▾" });
        Press(current, window);
        Grid(ribbon).Should().NotBeNull("the dropdown opens the grid");
    }

    [AvaloniaFact]
    public void The_grid_lists_every_choice_and_works_by_keyboard()
    {
        Ran.Clear();
        var ribbon = Show(Build(), out var window);
        var expand = Named(PanelOf(ribbon), "Show all: Styles");
        Press(expand, window);
        var tiles = GridTiles(ribbon);
        tiles.Select(Name).Should().Equal("Normal", "Heading 1", "Heading 2", "Title", "Quote", "Code");
        tiles.Select(t => global::Avalonia.Controls.Grid.GetColumn(t)).Should().Equal(new[] { 0, 1, 2, 0, 1, 2 }, "three columns, as declared");
        tiles[0].IsFocused.Should().BeTrue("focus goes to the current choice");

        Key(window, global::Avalonia.Input.Key.Right, PhysicalKey.ArrowRight);
        tiles[1].IsFocused.Should().BeTrue();
        Key(window, global::Avalonia.Input.Key.Down, PhysicalKey.ArrowDown);
        tiles[4].IsFocused.Should().BeTrue("Down moves a row");
        Key(window, global::Avalonia.Input.Key.Escape, PhysicalKey.Escape);
        Grid(ribbon).Should().BeNull("Escape closes the grid");
        Named(PanelOf(ribbon), "Show all: Styles").IsFocused.Should().BeTrue("and focus is back on its button");

        Press(Named(PanelOf(ribbon), "Show all: Styles"), window);
        Key(window, global::Avalonia.Input.Key.End, PhysicalKey.End);
        Key(window, global::Avalonia.Input.Key.Enter, PhysicalKey.Enter);
        Ran.Should().Equal("Code");
        Grid(ribbon).Should().BeNull("picking closes the grid");
        ribbon.GetGallerySelection("styles").Should().Be("code");
        Named(PanelOf(ribbon), "Show all: Styles").IsFocused.Should().BeTrue("Code is not in the row, so focus goes to the expand button");
    }

    [AvaloniaFact]
    public void A_small_group_shows_two_tiles()
    {
        var ribbon = Show(Build(RibbonGroupSize.Small), out _);
        RowTiles(PanelOf(ribbon)).Should().Equal("Normal", "Heading 1");
    }

    [AvaloniaFact]
    public void In_a_folded_group_the_grid_opens_over_its_popup_and_a_pick_closes_both()
    {
        Ran.Clear();
        var ribbon = Show(Build(RibbonGroupSize.Popup), out var window);
        var chunk = Shown(PanelOf(ribbon)).First(b => b.GetType().Name == "RibbonChunkButton" && RibbonTests.TipText(b) == "Styles");
        Press(chunk, window);
        chunk.Flyout!.IsOpen.Should().BeTrue();
        var inside = (Control)((Flyout)chunk.Flyout).Content!;
        Press(inside.GetVisualDescendants().OfType<Button>().First(b => Name(b) == "Show all: Styles"), window);
        Grid(ribbon).Should().NotBeNull("the grid opens from inside the popup");
        chunk.Flyout.IsOpen.Should().BeTrue("over it, without closing it");
        Press(GridTiles(ribbon).First(t => Name(t) == "Quote"), window);
        Ran.Should().Equal("Quote");
        Grid(ribbon).Should().BeNull();
        chunk.Flyout.IsOpen.Should().BeFalse("a pick closes the folded group, as a command does");
    }

    [AvaloniaFact]
    public void Stepping_inside_an_unpinned_reveal_keeps_it_open()
    {
        var ribbon = Build();
        ribbon.IsPinned = false;
        ribbon.IsCollapsed = true;
        Show(ribbon, out var window);
        Press(ribbon.GetVisualDescendants().OfType<Button>().First(b => Name(b) == "Home"), window);
        var overlay = ribbon.OpenOverlay;
        overlay.Should().NotBeNull("precondition: the reveal is open");
        Press(overlay!.GetVisualDescendants().OfType<Button>().First(b => Name(b) == "More choices" && b.IsEffectivelyEnabled), window);
        ribbon.OpenOverlay.Should().BeSameAs(overlay, "the gallery redraws in place");
        RowTiles(overlay!).Should().Contain("Code");
    }

    [AvaloniaFact]
    public void The_narrow_menu_lists_the_choices_with_the_current_one_marked()
    {
        Ran.Clear();
        var ribbon = Build();
        ribbon.NarrowThreshold = 600;
        Show(ribbon, out var window, 400);
        Press(ribbon.GetVisualDescendants().OfType<Button>().First(b => Name(b) == "Open the ribbon menu"), window, 400);
        var entries = ribbon.OpenOverlay!.GetVisualDescendants().OfType<Button>().Where(b => b.GetType().Name == "RibbonToggleCommand").ToList();
        entries.Select(Name).Should().Equal("Normal", "Heading 1", "Heading 2", "Title", "Quote", "Code");
        ((ISelectionItemProvider)ControlAutomationPeer.CreatePeerForElement(entries[0])).IsSelected.Should().BeTrue();
        Press(entries[3], window, 400);
        Ran.Should().Equal("Title");
        ribbon.GetGallerySelection("styles").Should().Be("title");
    }

    [AvaloniaFact]
    public void Key_tips_reach_the_tiles_and_the_grid()
    {
        Ran.Clear();
        var ribbon = Show(Build(), out var window);
        window.KeyPress(global::Avalonia.Input.Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        window.KeyRelease(global::Avalonia.Input.Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Settle(window);
        window.KeyPress(global::Avalonia.Input.Key.H, RawInputModifiers.None, PhysicalKey.H, "h");
        Settle(window);
        var tips = (IReadOnlyDictionary<string, Control>)typeof(Ribbon).GetProperty("KeyTips",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ribbon)!;
        tips.Values.Select(Name).Should().Contain(new[] { "Normal", "Title", "Show all: Styles" });
        string expand = tips.Single(t => Name(t.Value) == "Show all: Styles").Key;
        window.KeyPress((Key)((int)global::Avalonia.Input.Key.A + (expand[0] - 'A')), RawInputModifiers.None, PhysicalKey.None, expand.ToLowerInvariant());
        Settle(window);
        Grid(ribbon).Should().NotBeNull("the expand letter opens the grid");
        tips = (IReadOnlyDictionary<string, Control>)typeof(Ribbon).GetProperty("KeyTips",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ribbon)!;
        tips.Should().ContainKey("Q").WhoseValue.Should().Match<Control>(c => Name(c) == "Quote", "a declared letter in the grid");
        window.KeyPress(global::Avalonia.Input.Key.Q, RawInputModifiers.None, PhysicalKey.Q, "q");
        Settle(window);
        Ran.Should().Equal("Quote");
    }

    [AvaloniaFact]
    public void Capture_gallery_screenshots()
    {
        var dir = System.Environment.GetEnvironmentVariable("BIRKO_SHOTS") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "birko-xaml-shots");
        System.IO.Directory.CreateDirectory(dir);
        foreach (var style in new[] { RibbonGalleryStyle.Framed, RibbonGalleryStyle.Open, RibbonGalleryStyle.Dropdown })
        {
            var ribbon = Build(style: style);
            Show(ribbon, out var window);
            window.CaptureRenderedFrame()?.Save(System.IO.Path.Combine(dir, $"ribbon-gallery-{style.ToString().ToLowerInvariant()}.png"));
            if (style == RibbonGalleryStyle.Framed)
            {
                Press(Named(PanelOf(ribbon), "Show all: Styles"), window);
                window.CaptureRenderedFrame()?.Save(System.IO.Path.Combine(dir, "ribbon-gallery-grid.png"));
            }
            window.Close();
        }
    }
}
