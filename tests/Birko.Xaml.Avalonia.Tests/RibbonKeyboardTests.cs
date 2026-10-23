using Avalonia;
using Avalonia.Controls;
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
/// TASK-752 (FEATURE-019 D2 on the desktop): the keyboard model b-ribbon has had since TASK-731. The open panel is one
/// Tab stop; the arrows move by on-screen position across groups, wrapping left and right; ↑ from the top row returns
/// to the tab; Home/End; ↓ from a tab enters at the command focused last; ↓ on a folded group opens it; focus stays on
/// its command when the row rescales.
/// </summary>
public class RibbonKeyboardTests
{
    private static RibbonTab Home()
    {
        RibbonItem I(string label) => new() { Id = label, Label = label, Icon = "●" };
        return new RibbonTab
        {
            Id = "home", Label = "Home", Groups = new[]
            {
                new RibbonGroup { Label = "Clipboard", ScalingPriority = 3, Items = new[] { I("Paste"), I("Cut"), I("Copy") } },
                new RibbonGroup { Label = "Records", Items = new[] { I("New"), I("Delete"), I("Duplicate") } },
                new RibbonGroup { Label = "Layout", Items = new[] { I("Columns"), I("Rows") } },
            },
        };
    }

    private static Ribbon Show(Ribbon ribbon, out Window window, double width = 1000)
    {
        var page = new StackPanel();
        page.Children.Add(new Button { Content = "Before" });
        page.Children.Add(ribbon);
        page.Children.Add(new Button { Content = "After" });
        window = new Window { Content = page, Width = width, Height = 300 };
        window.Show();
        Layout(window, width);
        return ribbon;
    }

    private static void Layout(Window window, double width)
    {
        window.Width = width;
        window.Measure(new Size(width, 300));
        window.Arrange(new Rect(0, 0, width, 300));
        Dispatcher.UIThread.RunJobs();
    }

    private static Panel PanelOf(Ribbon ribbon) =>
        (Panel)ribbon.GetVisualDescendants().First(v => v.GetType().Name == "RibbonGroupsPanel");

    private static List<Button> Stops(Panel panel) =>
        panel.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyEnabled && b.TranslatePoint(default, panel) is { X: > -1000 }).ToList();

    private static Button Command(Panel panel, string name) =>
        Stops(panel).First(b => global::Avalonia.Automation.AutomationProperties.GetName(b) == name);

    private static string? Focused(Window window) =>
        window.FocusManager?.GetFocusedElement() is Control c ? global::Avalonia.Automation.AutomationProperties.GetName(c) : null;

    private static void Key_(Window window, Key key)
    {
        var target = (Control)window.FocusManager!.GetFocusedElement()!;
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = target });
        Dispatcher.UIThread.RunJobs();
    }

    private static void Focus(Button b)
    {
        b.Focus(NavigationMethod.Tab).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void The_open_panel_is_one_Tab_stop_that_remembers_the_command_focused_last()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out var window);
        var panel = PanelOf(ribbon);
        Stops(panel).Count(KeyboardNavigation.GetIsTabStop).Should().Be(1, "one stop for the whole panel");
        KeyboardNavigation.GetIsTabStop(Command(panel, "Paste")).Should().BeTrue("the first command, to begin with");

        Focus(Command(panel, "Paste"));
        Key_(window, Key.Right);
        Focused(window).Should().Be("New");
        KeyboardNavigation.GetIsTabStop(Command(panel, "New")).Should().BeTrue("the stop moves with focus");
        Stops(panel).Count(KeyboardNavigation.GetIsTabStop).Should().Be(1);

        var next = KeyboardNavigationHandler.GetNext(Command(panel, "New"), NavigationDirection.Next) as Control;
        next.Should().NotBeNull();
        next!.GetVisualAncestors().Should().NotContain(panel, "Tab from the panel leaves it");
    }

    [AvaloniaFact]
    public void Arrows_move_by_position_across_groups_and_wrap()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out var window);
        var panel = PanelOf(ribbon);
        Focus(Command(panel, "Cut"));
        Key_(window, Key.Right);
        Focused(window).Should().Be("Delete", "Right keeps the row: Cut is row 2, so is Delete");
        Key_(window, Key.Right);
        Focused(window).Should().Be("Rows");
        Key_(window, Key.Right);
        Focused(window).Should().Be("Paste", "Right past the last group wraps to the first command");
        Key_(window, Key.Left);
        Focused(window).Should().Be("Rows", "and Left from the first wraps to the last");
        Key_(window, Key.Down);
        Focused(window).Should().Be("Rows", "Down from the bottom command of a column stays (nothing below)");
        Key_(window, Key.Up);
        Focused(window).Should().Be("Columns", "Up stays in the column");
        Key_(window, Key.End);
        Focused(window).Should().Be("Rows");
        Key_(window, Key.Home);
        Focused(window).Should().Be("Paste");
    }

    [AvaloniaFact]
    public void Up_from_the_top_row_returns_to_the_tab_and_Down_from_the_tab_comes_back()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home(), new RibbonTab { Id = "view", Label = "View" } } }, out var window);
        var panel = PanelOf(ribbon);
        Focus(Command(panel, "Cut"));
        Key_(window, Key.Right);
        Key_(window, Key.Up);
        Focused(window).Should().Be("New");
        Key_(window, Key.Up);
        Focused(window).Should().Be("Home", "Up from the top row goes back to the active tab");
        Key_(window, Key.Down);
        Focused(window).Should().Be("New", "Down from the tab enters at the command focused last, not the first");
    }

    [AvaloniaFact]
    public void Down_on_a_folded_group_opens_it_and_moves_focus_inside()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() }, PreferredGroupSize = RibbonGroupSize.Popup }, out var window);
        var panel = PanelOf(ribbon);
        var chunk = Stops(panel).First(b => b.GetType().Name == "RibbonChunkButton"); // internal type
        Focus(chunk);
        Key_(window, Key.Down);
        Dispatcher.UIThread.RunJobs();
        chunk.Flyout!.IsOpen.Should().BeTrue("Down opens the folded group");
        Focused(window).Should().Be("Paste", "and focus goes to its first command");
    }

    [AvaloniaFact]
    public void Focus_stays_on_its_command_when_the_row_rescales()
    {
        // Narrow switch off: this is about rescaling, not about the ribbon becoming a menu.
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() }, NarrowThreshold = 0 }, out var window, 1000);
        var panel = PanelOf(ribbon);
        Focus(Command(panel, "Delete"));
        ribbon.ResolvedGroupSizes.Should().OnlyContain(s => s == RibbonGroupSize.Medium, "precondition");

        Layout(window, 220);
        Dispatcher.UIThread.RunJobs();
        ribbon.ResolvedGroupSizes.Should().Contain(s => s != RibbonGroupSize.Medium, "precondition: the row rescaled");
        var focused = window.FocusManager?.GetFocusedElement() as Control;
        focused.Should().NotBeNull("focus is not dropped out of the ribbon");
        focused!.GetVisualAncestors().Should().Contain(panel);
        // Either the same command at its new size, or, when Records folded, its group's button.
        Focused(window).Should().BeOneOf("Delete", "Records");
    }
}
