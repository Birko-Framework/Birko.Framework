using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Core.Ribbon;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>STORY-036: the Ribbon (b-ribbon / BAppShell chrome).</summary>
public class RibbonTests
{
    private static Ribbon Build(out bool[] ran)
    {
        var flags = new bool[1];
        var home = new RibbonTab
        {
            Id = "home", Label = "Home",
            Groups = new[]
            {
                new RibbonGroup { Label = "Clipboard", Items = new[]
                {
                    new RibbonItem { Id = "paste", Label = "Paste", Icon = "\U0001F4CB", Run = () => flags[0] = true },
                    new RibbonItem { Id = "cut", Label = "Cut", Icon = "✂" },
                }},
            },
        };
        var view = new RibbonTab
        {
            Id = "view", Label = "View",
            Groups = new[] { new RibbonGroup { Label = "Zoom", Items = new[] { new RibbonItem { Id = "zin", Label = "Zoom In" } } } },
        };
        ran = flags;
        return new Ribbon { Tabs = new[] { home, view } };
    }

    private static T Show<T>(T control, double width = 700) where T : Control
    {
        var window = new Window { Content = control, Width = width, Height = 200 };
        window.Show();
        window.Measure(new Size(width, 200));
        window.Arrange(new Rect(0, 0, width, 200));
        Dispatcher.UIThread.RunJobs();
        return control;
    }

    /// <summary>A ribbon with far more tabs and groups than fit a narrow window (TASK-097 overflow).</summary>
    private static Ribbon BuildCrowded()
    {
        RibbonItem I(string label) => new() { Id = label, Label = label, Icon = "●" };
        RibbonGroup G(string label) => new() { Label = label, Items = new[] { I(label + " A"), I(label + " B") } };

        var home = new RibbonTab
        {
            Id = "home", Label = "Home",
            Groups = new[] { G("Clipboard"), G("Records"), G("Layout"), G("Styles"), G("Review"), G("Export") },
        };
        var others = new[] { "Insert", "Design", "Transitions", "Animations", "SlideShow", "Review", "View", "Developer" }
            .Select(n => new RibbonTab { Id = n.ToLowerInvariant(), Label = n, Groups = new[] { G(n) } });

        return new Ribbon { Tabs = new[] { home }.Concat(others).ToArray() };
    }

    /// <summary>The scroller whose content holds <paramref name="text"/> — tab strip vs groups row.</summary>
    private static ScrollViewer ScrollerContaining(Control root, string text) =>
        root.GetVisualDescendants().OfType<ScrollViewer>()
            .First(s => s.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));

    private static Button ChevronByTip(Control root, string tip) =>
        root.GetVisualDescendants().OfType<Button>()
            .First(b => (b.GetValue(ToolTip.TipProperty) as string) == tip);

    /// <summary>
    /// A chevron is *shown* only when it is both in the layout and active. Once a row overflows, both
    /// slots stay in the layout (reserved) so the row never reflows; only opacity / hit-testing change.
    /// So IsVisible alone no longer answers "can the user see and click this".
    /// </summary>
    private static bool IsShown(Button chevron) =>
        chevron.IsVisible && chevron.Opacity > 0 && chevron.IsHitTestVisible;

    /// <summary>In the layout at all — i.e. holding its slot open, visible or not.</summary>
    private static bool IsReserved(Button chevron) => chevron.IsVisible;

    private static IEnumerable<string?> Texts(Control c) =>
        c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);

    [AvaloniaFact]
    public void Renders_tabs_and_active_group_items()
    {
        var ribbon = Show(Build(out _));
        Texts(ribbon).Should().Contain("Home");
        Texts(ribbon).Should().Contain("View");
        Texts(ribbon).Should().Contain("Clipboard");
        Texts(ribbon).Should().Contain("Paste");
    }

    [AvaloniaFact]
    public void Clicking_an_item_runs_its_action()
    {
        var ribbon = Show(Build(out var ran));
        var paste = ribbon.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Paste"));
        paste.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ran[0].Should().BeTrue();
    }

    [AvaloniaFact]
    public void Switching_tab_shows_the_other_group()
    {
        var ribbon = Build(out _);
        Show(ribbon);
        Texts(ribbon).Should().Contain("Clipboard");

        ribbon.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Texts(ribbon).Should().Contain("Zoom");
        Texts(ribbon).Should().NotContain("Clipboard", "the Home tab's groups are no longer shown");
    }

    [AvaloniaFact]
    public void Collapsing_hides_the_groups_but_keeps_the_tab_strip()
    {
        var ribbon = Show(Build(out _));
        Texts(ribbon).Should().Contain("Clipboard", "groups show when expanded");

        ribbon.IsCollapsed = true;
        Dispatcher.UIThread.RunJobs();

        Texts(ribbon).Should().NotContain("Clipboard", "groups are hidden when collapsed");
        Texts(ribbon).Should().Contain("Home", "the tab strip stays visible");
    }

    [AvaloniaFact]
    public void Clicking_the_active_tab_toggles_collapse()
    {
        var ribbon = Show(Build(out _)); // Home (index 0) is active
        var home = ribbon.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Home"));

        home.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ribbon.IsCollapsed.Should().BeTrue("clicking the active tab collapses the ribbon");
    }

    // ── TASK-097: overflow must stay reachable at a narrow width ──────────────────

    [AvaloniaFact]
    public void Tab_strip_scrolls_when_the_tabs_overflow()
    {
        var ribbon = Show(BuildCrowded(), width: 320);
        var scroller = ScrollerContaining(ribbon, "Home");

        scroller.Extent.Width.Should().BeGreaterThan(scroller.Viewport.Width,
            "9 tabs cannot fit 320px, so the strip must be scrollable rather than clipped");
        IsShown(ChevronByTip(ribbon, "Scroll tabs right")).Should().BeTrue(
            "an invisible overflow is the defect — there must be a visible affordance");
    }

    [AvaloniaFact]
    public void Groups_row_scrolls_when_the_groups_overflow()
    {
        var ribbon = Show(BuildCrowded(), width: 320);
        var scroller = ScrollerContaining(ribbon, "Clipboard");

        scroller.Extent.Width.Should().BeGreaterThan(scroller.Viewport.Width);
        IsShown(ChevronByTip(ribbon, "Scroll groups right")).Should().BeTrue();
        Texts(ribbon).Should().Contain("Export", "every group stays in the tree and is reachable by scrolling");
    }

    [AvaloniaFact]
    public void Clicking_the_chevron_scrolls_and_reveals_the_back_chevron()
    {
        var ribbon = Show(BuildCrowded(), width: 320);
        var scroller = ScrollerContaining(ribbon, "Home");
        var right = ChevronByTip(ribbon, "Scroll tabs right");
        var left = ChevronByTip(ribbon, "Scroll tabs left");

        IsShown(left).Should().BeFalse("nothing is scrolled off to the left yet");
        IsReserved(left).Should().BeTrue("but its slot is held open, so revealing it cannot reflow the row");

        double viewportBefore = scroller.Viewport.Width;
        double rightEdgeBefore = right.Bounds.X;

        right.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        scroller.Offset.X.Should().BeGreaterThan(0);
        IsShown(left).Should().BeTrue("scrolled content to the left is now reachable back");

        // The stability property: activating the back chevron must not reflow the row, or the forward
        // chevron moves out from under the pointer mid-click — the defect reported on the web side.
        scroller.Viewport.Width.Should().BeApproximately(viewportBefore, 0.5,
            "both slots were already reserved, so nothing resized");
        right.Bounds.X.Should().BeApproximately(rightEdgeBefore, 0.5, "the click target did not move");
    }

    [AvaloniaFact]
    public void Selecting_a_scrolled_tab_does_not_snap_the_strip_back_to_the_start()
    {
        // Reported from the gallery: scroll the strip to reach a far-right tab, click it — the tab opens
        // correctly but the strip jumps back to the first tab, so the tab just picked is off-screen.
        // Rebuild() discards the tree, so the new ScrollViewer started at offset 0 every time.
        var ribbon = Show(BuildCrowded(), width: 320);
        var scroller = ScrollerContaining(ribbon, "Home");

        var right = ChevronByTip(ribbon, "Scroll tabs right");
        for (int i = 0; i < 6; i++) // scroll to the far end
        {
            right.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }
        double scrolledTo = scroller.Offset.X;
        scrolledTo.Should().BeGreaterThan(0, "precondition: the strip is scrolled away from the start");

        var lastTab = ribbon.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Developer"));
        lastTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // Rebuild() replaced the tree, so re-resolve the scroller rather than reusing the stale one.
        var after = ScrollerContaining(ribbon, "Home");
        after.Offset.X.Should().BeApproximately(scrolledTo, 1.0,
            "the strip must stay where the user left it across the rebuild");
    }

    [AvaloniaFact]
    public void A_selection_made_from_offscreen_is_scrolled_into_view()
    {
        // The other half: restoring the offset alone would leave a keyboard/programmatic selection
        // off-screen, so a changed selection is brought into view (a no-op after a click, since the tab
        // clicked is already visible).
        var ribbon = Show(BuildCrowded(), width: 320);

        ribbon.SelectedIndex = 8; // "Developer" — the last tab, off-screen at 320px
        Dispatcher.UIThread.RunJobs();

        var scroller = ScrollerContaining(ribbon, "Home");
        scroller.Offset.X.Should().BeGreaterThan(0,
            "selecting an off-screen tab must scroll it into view, not leave it hidden");
    }

    [AvaloniaFact]
    public void No_chevrons_and_no_vertical_scrollbar_when_everything_fits()
    {
        var ribbon = Show(Build(out _), width: 900);

        IsReserved(ChevronByTip(ribbon, "Scroll tabs right")).Should().BeFalse("a row that fits reserves no slots");
        IsReserved(ChevronByTip(ribbon, "Scroll tabs left")).Should().BeFalse();
        IsReserved(ChevronByTip(ribbon, "Scroll groups right")).Should().BeFalse(
            "chevrons must cost no layout at a wide width");

        ribbon.GetVisualDescendants().OfType<ScrollViewer>().Should()
            .OnlyContain(s => s.VerticalScrollBarVisibility == global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                "a vertical bar would change the ribbon's height with the window width");
    }

    [AvaloniaFact]
    public void Collapse_chevron_stays_outside_the_tab_scroller()
    {
        var ribbon = Show(BuildCrowded(), width: 320);
        var tabScroller = ScrollerContaining(ribbon, "Home");

        tabScroller.GetVisualDescendants().OfType<Button>()
            .Should().NotContain(b => (b.GetValue(ToolTip.TipProperty) as string) == "Collapse the ribbon",
                "the collapse chevron must stay pinned and never scroll out of reach");
    }

    // ── TASK-098: the new model fields must not change rendering ──────────────────

    [AvaloniaFact]
    public void Setting_the_new_scaling_fields_does_not_change_what_renders_yet()
    {
        // TASK-098 is model + tokens only; the degrade pass is TASK-099. So a group carrying a
        // priority and a floor must still render exactly like one that carries neither — otherwise
        // the model landing would silently be a behaviour change.
        var plain = new RibbonGroup { Label = "Clipboard", Items = new[] { new RibbonItem { Id = "cut", Label = "Cut", Icon = "✂" } } };
        var annotated = new RibbonGroup
        {
            Label = "Clipboard",
            Icon = "📋",
            ScalingPriority = 10,
            MinSize = RibbonGroupSize.Small,
            Items = new[] { new RibbonItem { Id = "cut", Label = "Cut", Icon = "✂" } },
        };

        var before = Texts(Show(new Ribbon { Tabs = new[] { new RibbonTab { Id = "h", Label = "Home", Groups = new[] { plain } } } })).ToList();
        var after = Texts(Show(new Ribbon { Tabs = new[] { new RibbonTab { Id = "h", Label = "Home", Groups = new[] { annotated } } } })).ToList();

        after.Should().Equal(before, "the group icon is only drawn once a group collapses to Popup (TASK-100)");
    }

    [AvaloniaFact]
    public void Capture_ribbon_screenshot()
    {
        Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light;
        var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS")
                  ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
        Directory.CreateDirectory(dir);

        RibbonItem I(string label, string icon) => new() { Id = label, Label = label, Icon = icon };
        var ribbon = new Ribbon
        {
            Tabs = new[]
            {
                new RibbonTab { Id = "home", Label = "Home", Groups = new[]
                {
                    new RibbonGroup { Label = "Clipboard", Items = new[] { I("Paste", "\U0001F4CB"), I("Cut", "✂"), I("Copy", "\U0001F5D0") } },
                    new RibbonGroup { Label = "Records", Items = new[] { I("New", "➕"), I("Delete", "\U0001F5D1") } },
                }},
                new RibbonTab { Id = "view", Label = "View", Groups = new[] { new RibbonGroup { Label = "Zoom", Items = new[] { I("Zoom In", "\U0001F50D") } } } },
            },
        };
        var page = new Border { Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#F1F5F9")), Child = ribbon, Height = 120, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Content = page, Width = 700, Height = 160 };
        window.Show();
        window.Measure(new Size(700, 160));
        window.Arrange(new Rect(0, 0, 700, 160));
        Dispatcher.UIThread.RunJobs();

        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        frame?.Save(Path.Combine(dir, "ribbon.png"));

        // Collapsed (tabs-only) variant
        ribbon.IsCollapsed = true;
        window.Measure(new Size(700, 160));
        window.Arrange(new Rect(0, 0, 700, 160));
        Dispatcher.UIThread.RunJobs();
        global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)?
            .Save(Path.Combine(dir, "ribbon-collapsed.png"));

        ribbon.GetVisualDescendants().OfType<Button>().Should().NotBeEmpty();
    }
}
