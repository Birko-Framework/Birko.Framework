using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
/// TASK-753 (FEATURE-019 D9 on the desktop): the rich tooltip b-ribbon has had since TASK-738. Every command and folded
/// group shows its name, the shortcut beside it and the description under it, in the light look; after a 500ms hover
/// delay, and at once on keyboard focus; the first Escape hides it. The description and shortcut reach automation.
/// </summary>
public class RibbonTooltipTests
{
    private static RibbonTab Home() => new()
    {
        Id = "home", Label = "Home", Groups = new[]
        {
            new RibbonGroup { Label = "Clipboard", Icon = "📋", Items = new[]
            {
                new RibbonItem { Id = "paste", Label = "Paste", Icon = "📋", Description = "Add content from the clipboard.", Shortcut = "Ctrl+V" },
                new RibbonItem { Id = "cut", Label = "Cut", Icon = "✂" },
            } },
            new RibbonGroup { Label = "Records", Items = new[] { new RibbonItem { Id = "new", Label = "New", Icon = "➕" } } },
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

    private static List<Button> Shown(Visual scope) =>
        scope.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyEnabled && b.TranslatePoint(default, scope) is { X: > -1000 }).ToList();

    private static Button Command(Visual scope, string name) =>
        Shown(scope).First(b => global::Avalonia.Automation.AutomationProperties.GetName(b) == name);

    private static void Key_(Window window, Key key)
    {
        var target = (Control)window.FocusManager!.GetFocusedElement()!;
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = target });
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void A_command_tooltip_shows_its_name_shortcut_and_description()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out _);
        var tip = ToolTip.GetTip(Command(PanelOf(ribbon), "Paste")).Should().BeOfType<ToolTip>().Subject;
        var lines = ((Panel)tip.Content!).Children.OfType<TextBlock>().ToList();
        lines[0].Inlines!.OfType<Run>().Select(r => r.Text).Should().Equal("Paste", " (Ctrl+V)");
        lines[0].Inlines!.OfType<Run>().First().FontWeight.Should().Be(global::Avalonia.Media.FontWeight.SemiBold, "the name is the heading");
        lines[1].Text.Should().Be("Add content from the clipboard.");

        var plain = (ToolTip)ToolTip.GetTip(Command(PanelOf(ribbon), "Cut"))!;
        ((Panel)plain.Content!).Children.Should().ContainSingle("a command with no description is just its name");
    }

    [AvaloniaFact]
    public void The_tooltip_uses_the_light_look_and_a_500ms_delay()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out _);
        var paste = Command(PanelOf(ribbon), "Paste");
        var tip = (ToolTip)ToolTip.GetTip(paste)!;
        ribbon.TryFindResource("BBgElevatedBrush", out var elevated).Should().BeTrue("the theme is loaded");
        ribbon.TryFindResource("BTooltipBgBrush", out var dark).Should().BeTrue();
        tip.Background.Should().BeSameAs(elevated, "the ribbon's tooltip is the light one, as on the web");
        tip.Background.Should().NotBeSameAs(dark);
        Shown(PanelOf(ribbon)).Should().OnlyContain(b => ToolTip.GetShowDelay(b) == 500, "every command waits 500ms on hover");
    }

    [AvaloniaFact]
    public void The_description_and_shortcut_reach_automation()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out _);
        var paste = Command(PanelOf(ribbon), "Paste");
        global::Avalonia.Automation.AutomationProperties.GetHelpText(paste).Should().Be("Add content from the clipboard.");
        global::Avalonia.Automation.AutomationProperties.GetAcceleratorKey(paste).Should().Be("Ctrl+V");
    }

    [AvaloniaFact]
    public void Keyboard_focus_shows_it_at_once_and_a_click_does_not()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out _);
        var paste = Command(PanelOf(ribbon), "Paste");
        paste.Focus(NavigationMethod.Pointer);
        Dispatcher.UIThread.RunJobs();
        ToolTip.GetIsOpen(paste).Should().BeFalse("focus by a click does not open it");
        Command(PanelOf(ribbon), "Cut").Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        paste.Focus(NavigationMethod.Directional);
        Dispatcher.UIThread.RunJobs();
        ToolTip.GetIsOpen(paste).Should().BeTrue("keyboard focus opens it at once");
        Command(PanelOf(ribbon), "Cut").Focus(NavigationMethod.Directional);
        Dispatcher.UIThread.RunJobs();
        ToolTip.GetIsOpen(paste).Should().BeFalse("and it closes when focus leaves");
    }

    [AvaloniaFact]
    public void The_first_Escape_hides_the_tooltip_and_the_next_goes_back_to_the_tab()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out var window);
        var paste = Command(PanelOf(ribbon), "Paste");
        paste.Focus(NavigationMethod.Directional);
        Dispatcher.UIThread.RunJobs();
        ToolTip.GetIsOpen(paste).Should().BeTrue("precondition");
        Key_(window, Key.Escape);
        ToolTip.GetIsOpen(paste).Should().BeFalse("the first Escape hides the tooltip");
        paste.IsFocused.Should().BeTrue("and leaves focus where it was");
        Key_(window, Key.Escape);
        global::Avalonia.Automation.AutomationProperties.GetName((Control)window.FocusManager!.GetFocusedElement()!)
            .Should().Be("Home", "the next Escape goes back to the active tab, as b-ribbon's panel does");
    }

    [AvaloniaFact]
    public void A_folded_group_tooltip_names_the_group_and_its_popup_takes_Escape_for_the_tip_first()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() }, PreferredGroupSize = RibbonGroupSize.Popup }, out var window);
        var chunk = Shown(PanelOf(ribbon)).First(b => b.GetType().Name == "RibbonChunkButton");
        RibbonTests.TipText(chunk).Should().Be("Clipboard");
        var flyout = (Flyout)chunk.Flyout!;
        flyout.ShowAt(chunk);
        Dispatcher.UIThread.RunJobs();
        var inside = Command((Visual)flyout.Content!, "Paste");
        Command((Visual)flyout.Content!, "Cut").Focus(NavigationMethod.Directional); // so focusing Paste is a change
        Dispatcher.UIThread.RunJobs();
        inside.Focus(NavigationMethod.Directional);
        Dispatcher.UIThread.RunJobs();
        ToolTip.GetIsOpen(inside).Should().BeTrue("precondition");
        Key_(window, Key.Escape);
        flyout.IsOpen.Should().BeTrue("the first Escape only hides the tooltip");
        Key_(window, Key.Escape);
        flyout.IsOpen.Should().BeFalse("the next one closes the popup");
    }
}
