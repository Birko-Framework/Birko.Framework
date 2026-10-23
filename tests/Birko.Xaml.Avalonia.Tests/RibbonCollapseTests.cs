using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Core.Ribbon;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// TASK-757 (FEATURE-019 D6 on the desktop): a double-click on a tab collapses the ribbon, or pins it back open, as in
/// Office and as b-ribbon has since TASK-735; it shares one toggle with Ctrl+F1, which is rebindable or off. Clicks come
/// from the headless mouse, so the double-click is the input system's own (ClickCount), not a faked event.
/// </summary>
public class RibbonCollapseTests
{
    // Top-aligned, as an app docks it: filling the window, a collapsed ribbon centred its tab strip vertically, so the
    // tab moved ~100px between the two clicks and they were not a double-click (measured).
    private static Ribbon Build() => new()
    {
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        Tabs = new[]
        {
            new RibbonTab { Id = "home", Label = "Home", Groups = new[] { new RibbonGroup { Label = "G", Items = new[] { new RibbonItem { Id = "a", Label = "A", Icon = "●" } } } } },
            new RibbonTab { Id = "view", Label = "View", Groups = new[] { new RibbonGroup { Label = "Z", Items = new[] { new RibbonItem { Id = "z", Label = "Zoom", Icon = "●" } } } } },
        },
    };

    private static Ribbon Show(Ribbon ribbon, out Window window)
    {
        window = new Window { Content = ribbon, Width = 800, Height = 240 };
        window.Show();
        window.Measure(new Size(800, 240));
        window.Arrange(new Rect(0, 0, 800, 240));
        Dispatcher.UIThread.RunJobs();
        return ribbon;
    }

    private static Point CentreOfTab(Ribbon ribbon, Window window, string name)
    {
        var tab = ribbon.GetVisualDescendants().OfType<Button>()
            .First(b => global::Avalonia.Automation.AutomationProperties.GetName(b) == name);
        return tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), window)!.Value;
    }

    private static void Click(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void DoubleClick(Ribbon ribbon, Window window, string tab)
    {
        // The second press is re-aimed: the first click rebuilds the strip, so its button is a new one.
        Click(window, CentreOfTab(ribbon, window, tab));
        Click(window, CentreOfTab(ribbon, window, tab));
    }

    private static void Key(Window window, Key key, KeyModifiers mods)
    {
        window.KeyPress(key, mods.HasFlag(KeyModifiers.Control) && mods.HasFlag(KeyModifiers.Shift)
            ? RawInputModifiers.Control | RawInputModifiers.Shift
            : mods.HasFlag(KeyModifiers.Control) ? RawInputModifiers.Control : RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void A_double_click_on_the_active_tab_collapses_and_a_second_brings_it_back()
    {
        var ribbon = Show(Build(), out var window);
        ribbon.IsCollapsed.Should().BeFalse("precondition");
        DoubleClick(ribbon, window, "Home");
        ribbon.IsCollapsed.Should().BeTrue("a double-click collapses the ribbon to its tabs");
        ribbon.IsPinned.Should().BeTrue();
        DoubleClick(ribbon, window, "Home");
        ribbon.IsCollapsed.Should().BeFalse("and another brings it back, pinned open");
    }

    [AvaloniaFact]
    public void A_double_click_on_another_tab_selects_it_and_collapses()
    {
        var ribbon = Show(Build(), out var window);
        DoubleClick(ribbon, window, "View");
        ribbon.SelectedIndex.Should().Be(1, "the first click still selects");
        ribbon.IsCollapsed.Should().BeTrue("the double-click collapses");
    }

    [AvaloniaFact]
    public void A_double_click_on_an_unpinned_ribbon_pins_it_open()
    {
        var ribbon = Build();
        ribbon.IsPinned = false;
        Show(ribbon, out var window);
        DoubleClick(ribbon, window, "Home");
        ribbon.IsPinned.Should().BeTrue("the toggle brings it back pinned, as b-ribbon's does");
        ribbon.IsCollapsed.Should().BeFalse();
        ribbon.CloseOverlay();
    }

    [AvaloniaFact]
    public void A_single_click_keeps_its_behaviour()
    {
        var ribbon = Show(Build(), out var window);
        Click(window, CentreOfTab(ribbon, window, "View"));
        ribbon.SelectedIndex.Should().Be(1);
        ribbon.IsCollapsed.Should().BeFalse("a single click on another tab selects it and nothing else");
    }

    [AvaloniaFact]
    public void The_shortcut_shares_the_toggle_and_is_rebindable_or_off()
    {
        var ribbon = Show(Build(), out var window);
        Key(window, global::Avalonia.Input.Key.F1, KeyModifiers.Control);
        ribbon.IsCollapsed.Should().BeTrue("Ctrl+F1 by default");
        Key(window, global::Avalonia.Input.Key.F1, KeyModifiers.Control);
        ribbon.IsCollapsed.Should().BeFalse();

        ribbon.CollapseShortcut = new KeyGesture(global::Avalonia.Input.Key.F1, KeyModifiers.Control | KeyModifiers.Shift);
        Key(window, global::Avalonia.Input.Key.F1, KeyModifiers.Control);
        ribbon.IsCollapsed.Should().BeFalse("Ctrl+F1 no longer does it");
        Key(window, global::Avalonia.Input.Key.F1, KeyModifiers.Control | KeyModifiers.Shift);
        ribbon.IsCollapsed.Should().BeTrue("the rebound shortcut does");

        ribbon.CollapseShortcut = null;
        Key(window, global::Avalonia.Input.Key.F1, KeyModifiers.Control | KeyModifiers.Shift);
        ribbon.IsCollapsed.Should().BeTrue("off: nothing toggles");

        ribbon.IsPinned = false;
        ribbon.ToggleRibbon();
        ribbon.IsPinned.Should().BeTrue("ToggleRibbon on an unpinned ribbon pins it open");
        ribbon.IsCollapsed.Should().BeFalse();
    }
}
