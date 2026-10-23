using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Core.Ribbon;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// TASK-759 (FEATURE-019 D8 on the desktop): the Quick Access Toolbar, as b-ribbon has had since TASK-737. Opt-in
/// defaults as icon buttons; a right-click on a ribbon command adds it and on a toolbar button removes it; the ▾
/// customises and resets; the app saves the choice as references (the user's decision) and restores it; three
/// positions; one Tab stop with arrows; digits as key tips; a toggle's toolbar copy keeps in step.
/// </summary>
public class RibbonQuickAccessTests
{
    private static readonly List<string> Ran = new();

    private static RibbonTab Home() => new()
    {
        Id = "home", Label = "Home", Groups = new[]
        {
            new RibbonGroup { Label = "Clipboard", Icon = "📋", Items = new[]
            {
                new RibbonItem { Id = "paste", Label = "Paste", Icon = "📋", Run = () => Ran.Add("paste") },
                new RibbonItem { Id = "cut", Label = "Cut", Icon = "✂", Run = () => Ran.Add("cut") },
                new RibbonItem { Id = "bold", Label = "Bold", Icon = "B", Toggle = true },
            } },
        },
    };

    private static RibbonItem[] Defaults() => new[]
    {
        new RibbonItem { Id = "save", Label = "Save", Icon = "💾", Run = () => Ran.Add("save") },
        new RibbonItem { Id = "undo", Label = "Undo", Icon = "↶", Run = () => Ran.Add("undo") },
    };

    private static Ribbon Build(bool qat = true)
    {
        var ribbon = new Ribbon { VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top, Tabs = new[] { Home() } };
        if (qat) ribbon.SetQuickAccess(Defaults());
        return ribbon;
    }

    private static Ribbon Show(Ribbon ribbon, out Window window)
    {
        window = new Window { Content = ribbon, Width = 900, Height = 260 };
        window.Show();
        Settle(window);
        return ribbon;
    }

    private static void Settle(Window window)
    {
        window.Measure(new Size(900, 260));
        window.Arrange(new Rect(0, 0, 900, 260));
        for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
    }

    private static T Internal<T>(Ribbon ribbon, string property) => (T)typeof(Ribbon).GetProperty(property,
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ribbon)!;

    private static string? Name(Control c) => global::Avalonia.Automation.AutomationProperties.GetName(c);

    private static StackPanel Bar(Ribbon ribbon) =>
        ribbon.GetVisualDescendants().OfType<StackPanel>().Single(p => Name(p) == "Quick Access Toolbar");

    private static List<Button> BarButtons(Ribbon ribbon) => Bar(ribbon).Children.OfType<Button>().ToList();

    private static List<string?> BarNames(Ribbon ribbon) => BarButtons(ribbon).Select(Name).ToList();

    private static Button Command(Ribbon ribbon, string name) =>
        ribbon.GetVisualDescendants().OfType<Button>()
            .First(b => b.IsEffectivelyEnabled && b.Tag is string t && t.StartsWith("item:") && Name(b) == name);

    private static void Press(Button b, Window window)
    {
        ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(b)).Invoke();
        Settle(window);
    }

    private static void RightClick(Control c, Window window)
    {
        var at = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        Settle(window);
    }

    private static void Pick(MenuFlyout menu, string header, Window window)
    {
        menu.Items.OfType<MenuItem>().Single(m => (m.Header as string) == header).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Settle(window);
    }

    [AvaloniaFact]
    public void Without_SetQuickAccess_there_is_no_toolbar_and_no_add_menu()
    {
        var ribbon = Show(Build(qat: false), out var window);
        ribbon.IsQuickAccessEnabled.Should().BeFalse();
        ribbon.GetVisualDescendants().OfType<StackPanel>().Should().NotContain(p => Name(p) == "Quick Access Toolbar");
        RightClick(Command(ribbon, "Cut"), window);
        Internal<MenuFlyout?>(ribbon, "QuickAccessMenu").Should().BeNull("an app without the toolbar offers nothing to add");
    }

    [AvaloniaFact]
    public void Defaults_are_icon_buttons_before_the_tabs_and_run_on_press()
    {
        Ran.Clear();
        var ribbon = Show(Build(), out var window);
        BarNames(ribbon).Should().Equal("Save", "Undo", "Customize Quick Access Toolbar");
        ribbon.QuickAccess.Should().Equal("qat:save", "qat:undo");
        var save = BarButtons(ribbon)[0];
        RibbonTests.TipText(save).Should().Contain("Save", "a rich tip names the icon");
        var home = ribbon.GetVisualDescendants().OfType<Button>().First(b => Name(b) == "Home");
        save.TranslatePoint(default, ribbon)!.Value.X.Should().BeLessThan(home.TranslatePoint(default, ribbon)!.Value.X, "in the tab row, before the tabs");
        Press(save, window);
        Ran.Should().Equal("save");
    }

    [AvaloniaFact]
    public void Right_click_on_a_command_adds_it_and_again_removes_it()
    {
        Ran.Clear();
        var ribbon = Show(Build(), out var window);
        var changes = new List<IReadOnlyList<string>>();
        ribbon.QuickAccessChanged += (_, refs) => changes.Add(refs);

        RightClick(Command(ribbon, "Cut"), window);
        Internal<MenuFlyout?>(ribbon, "QuickAccessMenu")!.Items.OfType<MenuItem>().Select(m => m.Header).Should().Equal("Add to Quick Access Toolbar");
        Pick(Internal<MenuFlyout?>(ribbon, "QuickAccessMenu")!, "Add to Quick Access Toolbar", window);
        BarNames(ribbon).Should().Equal("Save", "Undo", "Cut", "Customize Quick Access Toolbar");
        changes.Should().ContainSingle().Which.Should().Equal("qat:save", "qat:undo", "item:home/0/cut");
        Press(BarButtons(ribbon)[2], window);
        Ran.Should().Equal(new[] { "cut" }, "the added button runs the ribbon's command");

        RightClick(Command(ribbon, "Cut"), window);
        Pick(Internal<MenuFlyout?>(ribbon, "QuickAccessMenu")!, "Remove from Quick Access Toolbar", window);
        BarNames(ribbon).Should().Equal("Save", "Undo", "Customize Quick Access Toolbar");
    }

    [AvaloniaFact]
    public void Right_click_on_a_toolbar_button_removes_it()
    {
        var ribbon = Show(Build(), out var window);
        RightClick(BarButtons(ribbon)[0], window);
        Pick(Internal<MenuFlyout?>(ribbon, "QuickAccessMenu")!, "Remove from Quick Access Toolbar", window);
        ribbon.QuickAccess.Should().Equal("qat:undo");
    }

    [AvaloniaFact]
    public void The_customize_menu_hides_and_shows_defaults_in_place_and_resets()
    {
        var ribbon = Show(Build(), out var window);
        var more = BarButtons(ribbon).Last();
        Press(more, window);
        var menu = (MenuFlyout)more.Flyout!;
        menu.Items.OfType<MenuItem>().Select(m => (m.Header as string, m.IsChecked)).Should().Equal(("Save", true), ("Undo", true), ("Reset", false));
        Pick(menu, "Save", window);
        ribbon.QuickAccess.Should().Equal("qat:undo");

        ribbon.SetQuickAccessRefs(new[] { "qat:undo", "item:home/0/cut" });
        more = BarButtons(ribbon).Last();
        Press(more, window);
        menu = (MenuFlyout)more.Flyout!;
        menu.Items.OfType<MenuItem>().Select(m => m.Header as string).Should().Equal("Save", "Undo", "Cut", "Reset");
        Pick(menu, "Save", window);
        ribbon.QuickAccess.Should().Equal(new[] { "qat:save", "qat:undo", "item:home/0/cut" }, "a default shown again goes back to its place");

        more = BarButtons(ribbon).Last();
        Press(more, window);
        Pick((MenuFlyout)more.Flyout!, "Reset", window);
        ribbon.QuickAccess.Should().Equal("qat:save", "qat:undo");
    }

    [AvaloniaFact]
    public void Saved_references_restore_drop_what_no_longer_resolves_and_raise_nothing()
    {
        var ribbon = Show(Build(), out _);
        int changes = 0;
        ribbon.QuickAccessChanged += (_, _) => changes++;
        ribbon.SetQuickAccessRefs(new[] { "item:home/0/paste", "qat:gone", "item:home/9/cut", "item:home/0/paste", "qat:save" });
        ribbon.QuickAccess.Should().Equal("item:home/0/paste", "qat:save");
        BarNames(ribbon).Should().Equal("Paste", "Save", "Customize Quick Access Toolbar");
        changes.Should().Be(0, "restoring is the app's own call");
        ribbon.SetQuickAccessRefs(null);
        ribbon.QuickAccess.Should().Equal("qat:save", "qat:undo");
    }

    [AvaloniaTheory]
    [InlineData(RibbonQuickAccessPosition.Above)]
    [InlineData(RibbonQuickAccessPosition.Below)]
    public void The_toolbar_can_sit_on_its_own_row_above_or_below(RibbonQuickAccessPosition position)
    {
        var ribbon = Build();
        ribbon.QuickAccessPosition = position;
        Show(ribbon, out _);
        double barY = Bar(ribbon).TranslatePoint(default, ribbon)!.Value.Y;
        var home = ribbon.GetVisualDescendants().OfType<Button>().First(b => Name(b) == "Home");
        double tabY = home.TranslatePoint(default, ribbon)!.Value.Y;
        var cut = Command(ribbon, "Cut");
        double groupsY = cut.TranslatePoint(default, ribbon)!.Value.Y;
        if (position == RibbonQuickAccessPosition.Above) barY.Should().BeLessThan(tabY);
        else barY.Should().BeGreaterThan(groupsY);
    }

    [AvaloniaFact]
    public void The_toolbar_is_one_tab_stop_with_arrows_inside()
    {
        var ribbon = Show(Build(), out var window);
        var buttons = BarButtons(ribbon);
        buttons.Count(KeyboardNavigation.GetIsTabStop).Should().Be(1);
        buttons[0].Focus(NavigationMethod.Tab);
        Settle(window);
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        Settle(window);
        buttons[1].IsFocused.Should().BeTrue();
        window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
        Settle(window);
        buttons[^1].IsFocused.Should().BeTrue("End reaches the customize button");
        buttons.Where(KeyboardNavigation.GetIsTabStop).Should().Equal(buttons[^1]);
    }

    [AvaloniaFact]
    public void Key_tips_number_the_toolbar()
    {
        Ran.Clear();
        var ribbon = Show(Build(), out var window);
        window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Settle(window);
        Internal<IReadOnlyDictionary<string, Control>>(ribbon, "KeyTips").Keys.Should().Contain(new[] { "1", "2", "H" });
        window.KeyPress(Key.D2, RawInputModifiers.None, PhysicalKey.Digit2, "2");
        window.KeyRelease(Key.D2, RawInputModifiers.None, PhysicalKey.Digit2, "2");
        Settle(window);
        Ran.Should().Equal("undo");
    }

    [AvaloniaFact]
    public void A_toggle_on_the_toolbar_keeps_in_step_with_the_ribbon()
    {
        var ribbon = Show(Build(), out var window);
        ribbon.SetQuickAccessRefs(new[] { "item:home/0/bold" });
        Settle(window);
        Press(BarButtons(ribbon)[0], window);
        ribbon.IsPressed("bold", "home").Should().BeTrue();
        var ribbonCopy = Command(ribbon, "Bold");
        ((IToggleProvider)ControlAutomationPeer.CreatePeerForElement(ribbonCopy)).ToggleState.Should().Be(ToggleState.On);
    }

    [AvaloniaFact]
    public void Capture_quick_access_screenshot()
    {
        var dir = System.Environment.GetEnvironmentVariable("BIRKO_SHOTS") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "birko-xaml-shots");
        System.IO.Directory.CreateDirectory(dir);
        var ribbon = Show(Build(), out var window);
        ribbon.SetQuickAccessRefs(new[] { "qat:save", "qat:undo", "item:home/0/cut" });
        Settle(window);
        window.CaptureRenderedFrame()?.Save(System.IO.Path.Combine(dir, "ribbon-qat.png"));
    }
}
