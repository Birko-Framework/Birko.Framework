using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Core.Ribbon;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// TASK-755 (FEATURE-019 D3 on the desktop): split and menu buttons, as b-ribbon has had since TASK-732. A menu button
/// opens its menu; a split button runs its command and its ▾ part ("More options: Paste") opens the menu; choices carry
/// icon, label, shortcut, separators and a disabled state; a choice runs and reports itself with its parent; Alt+↓
/// opens; it works inside a folded group and in the narrow menu.
/// </summary>
public class RibbonMenuTests
{
    private static readonly List<string> Ran = new();

    private static RibbonTab Home() => new()
    {
        Id = "home", Label = "Home", Groups = new[]
        {
            new RibbonGroup { Label = "Clipboard", Icon = "📋", Items = new[]
            {
                new RibbonItem
                {
                    Id = "paste", Label = "Paste", Icon = "📋", Split = true, Run = () => Ran.Add("paste"),
                    Menu = new RibbonMenuEntry[]
                    {
                        new RibbonMenuItem { Id = "paste-text", Label = "Paste as text", Icon = "📝", Shortcut = "Ctrl+Shift+V", Run = () => Ran.Add("paste-text") },
                        RibbonMenuEntry.Separator,
                        new RibbonMenuItem { Id = "paste-pic", Label = "Paste as picture", Disabled = true },
                    },
                },
                new RibbonItem { Id = "cut", Label = "Cut", Icon = "✂" },
            } },
            new RibbonGroup { Label = "Export", Icon = "📤", Items = new[]
            {
                new RibbonItem { Id = "csv", Label = "CSV", Icon = "📊", Menu = new RibbonMenuEntry[] { new RibbonMenuItem { Id = "csv-comma", Label = "Comma separated" } } },
            } },
        },
    };

    private static Ribbon Show(Ribbon ribbon, out Window window, double width = 1000)
    {
        window = new Window { Content = ribbon, Width = width, Height = 240 };
        window.Show();
        window.Measure(new Size(width, 240));
        window.Arrange(new Rect(0, 0, width, 240));
        Dispatcher.UIThread.RunJobs();
        return ribbon;
    }

    private static Panel PanelOf(Ribbon ribbon) =>
        (Panel)ribbon.GetVisualDescendants().First(v => v.GetType().Name == "RibbonGroupsPanel");

    private static Button Shown(Visual scope, string name) =>
        scope.GetVisualDescendants().OfType<Button>()
            .First(b => b.IsEffectivelyEnabled && b.TranslatePoint(default, scope) is { X: > -1000 }
                && global::Avalonia.Automation.AutomationProperties.GetName(b) == name);

    private static void Click(Interactive c, RoutedEvent? ev = null)
    {
        // A button through its automation peer, as a press does: raising Click by hand skips the button's own
        // OnClick, which is where a Button opens its Flyout.
        if (c is Button b && ev is null) ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(b)).Invoke();
        else c.RaiseEvent(new RoutedEventArgs(ev ?? Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static List<MenuItem> Choices(Button trigger) => ((MenuFlyout)trigger.Flyout!).Items.OfType<MenuItem>().ToList();

    [AvaloniaFact]
    public void A_menu_button_opens_its_menu_of_choices()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out _);
        var csv = Shown(PanelOf(ribbon), "CSV");
        csv.GetType().Name.Should().Be("RibbonMenuButton");
        csv.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain("▾", "a menu button shows its caret");
        Click(csv);
        csv.Flyout!.IsOpen.Should().BeTrue("the whole button opens the menu");
        Choices(csv).Select(m => m.Header).Should().Equal("Comma separated");
    }

    [AvaloniaFact]
    public void A_split_runs_its_command_and_its_arrow_opens_the_menu()
    {
        Ran.Clear();
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out _);
        var main = Shown(PanelOf(ribbon), "Paste");
        var arrow = Shown(PanelOf(ribbon), "More options: Paste");
        Click(main);
        Ran.Should().Equal("paste");
        arrow.Flyout!.IsOpen.Should().BeFalse("the main part runs the command, it does not open the menu");

        Click(arrow);
        arrow.Flyout!.IsOpen.Should().BeTrue();
        var menu = (MenuFlyout)arrow.Flyout!;
        menu.Items.OfType<Separator>().Should().ContainSingle("separators between sets of choices");
        var choices = Choices(arrow);
        choices.Select(m => m.Header).Should().Equal("Paste as text", "Paste as picture");
        choices[0].Icon.Should().BeOfType<TextBlock>().Which.Text.Should().Be("📝");
        choices[0].InputGesture.Should().Be(KeyGesture.Parse("Ctrl+Shift+V"), "the shortcut is shown right-aligned");
        choices[1].IsEnabled.Should().BeFalse("a disabled choice cannot be picked");
    }

    [AvaloniaFact]
    public void A_choice_runs_and_reports_itself_with_its_parent()
    {
        Ran.Clear();
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out _);
        var events = new List<RibbonMenuItemInvokedEventArgs>();
        ribbon.MenuItemInvoked += (_, e) => events.Add(e);
        var arrow = Shown(PanelOf(ribbon), "More options: Paste");
        Click(arrow);
        Click(Choices(arrow)[0], MenuItem.ClickEvent);
        Ran.Should().Equal("paste-text");
        events.Should().ContainSingle().Which.Should().BeEquivalentTo(new { TabId = "home", ItemId = "paste-text", ParentId = "paste" });
    }

    [AvaloniaFact]
    public void Alt_Down_opens_the_menu_and_the_arrow_reports_its_state()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out _);
        var main = Shown(PanelOf(ribbon), "Paste");
        var arrow = Shown(PanelOf(ribbon), "More options: Paste");
        var peer = ControlAutomationPeer.CreatePeerForElement(arrow).Should().BeAssignableTo<IExpandCollapseProvider>().Subject;
        peer.ExpandCollapseState.Should().Be(global::Avalonia.Automation.ExpandCollapseState.Collapsed);
        main.Focus();
        main.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down, KeyModifiers = KeyModifiers.Alt, Source = main });
        Dispatcher.UIThread.RunJobs();
        arrow.Flyout!.IsOpen.Should().BeTrue("Alt+Down on the main part opens the menu, as in Office");
        peer.ExpandCollapseState.Should().Be(global::Avalonia.Automation.ExpandCollapseState.Expanded);
    }

    [AvaloniaFact]
    public void Both_parts_of_a_split_are_reached_by_the_arrows()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out var window);
        var main = Shown(PanelOf(ribbon), "Paste");
        main.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        main.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right, Source = main });
        Dispatcher.UIThread.RunJobs();
        global::Avalonia.Automation.AutomationProperties.GetName((Control)window.FocusManager!.GetFocusedElement()!)
            .Should().Be("More options: Paste", "the ▾ part is a stop of its own, right of the main part");
    }

    [AvaloniaFact]
    public void A_Large_split_stacks_its_parts_with_the_name_under_the_icon()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() }, PreferredGroupSize = RibbonGroupSize.Large }, out _);
        var main = Shown(PanelOf(ribbon), "Paste");
        var arrow = Shown(PanelOf(ribbon), "More options: Paste");
        var p = PanelOf(ribbon);
        arrow.TranslatePoint(default, p)!.Value.Y.Should().BeGreaterThan(main.TranslatePoint(default, p)!.Value.Y, "the ▾ part sits under the main part");
        arrow.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain(new[] { "Paste", "▾" });
        main.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().NotContain("Paste", "the main part is the icon");
    }

    [AvaloniaFact]
    public void A_menu_opens_from_a_folded_groups_popup()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() }, PreferredGroupSize = RibbonGroupSize.Popup }, out _);
        var chunk = PanelOf(ribbon).GetVisualDescendants().OfType<Button>()
            .First(b => b.GetType().Name == "RibbonChunkButton" && b.IsEffectivelyEnabled && RibbonTests.TipText(b) == "Clipboard");
        var popup = (Flyout)chunk.Flyout!;
        popup.ShowAt(chunk);
        Dispatcher.UIThread.RunJobs();
        var arrow = Shown((Visual)popup.Content!, "More options: Paste");
        Click(arrow);
        arrow.Flyout!.IsOpen.Should().BeTrue();
        popup.IsOpen.Should().BeTrue("the group's popup stays open under its menu");
    }

    [AvaloniaFact]
    public void The_narrow_menu_lists_choices_under_their_command()
    {
        Ran.Clear();
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() }, NarrowThreshold = 2000 }, out _);
        var events = new List<RibbonMenuItemInvokedEventArgs>();
        ribbon.MenuItemInvoked += (_, e) => events.Add(e);
        Click(ribbon.GetVisualDescendants().OfType<Button>().First(b => RibbonTests.TipText(b) == "Open the ribbon menu"));
        var overlay = ribbon.OpenOverlay!;
        var texts = overlay.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.Should().ContainInOrder("📋  Paste", "📝  Paste as text", "Paste as picture", "✂  Cut", "CSV", "Comma separated");
        overlay.GetVisualDescendants().OfType<Button>().First(b => global::Avalonia.Automation.AutomationProperties.GetName(b) == "Paste as picture")
            .IsEffectivelyEnabled.Should().BeFalse("a disabled choice is greyed there too");
        overlay.GetVisualDescendants().OfType<Button>().First(b => global::Avalonia.Automation.AutomationProperties.GetName(b) == "Paste as text")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Ran.Should().Equal("paste-text");
        events.Should().ContainSingle().Which.ParentId.Should().Be("paste");
    }

    [AvaloniaFact]
    public void Capture_menu_screenshots()
    {
        var dir = System.Environment.GetEnvironmentVariable("BIRKO_SHOTS") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "birko-xaml-shots");
        System.IO.Directory.CreateDirectory(dir);
        foreach (var (size, name) in new[] { (RibbonGroupSize.Medium, "medium"), (RibbonGroupSize.Large, "large") })
        {
            Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light;
            var ribbon = new Ribbon { Tabs = new[] { Home() }, PreferredGroupSize = size, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top };
            var window = new Window { Content = ribbon, Width = 520, Height = 180 };
            window.Show();
            window.Measure(new Size(520, 180));
            window.Arrange(new Rect(0, 0, 520, 180));
            Dispatcher.UIThread.RunJobs();
            global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)?.Save(System.IO.Path.Combine(dir, $"ribbon-menus-{name}.png"));
            window.Close();
        }
    }
}
