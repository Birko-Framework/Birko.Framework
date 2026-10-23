using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
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
/// TASK-756 (FEATURE-019 D4 on the desktop): the group dialog launcher, as b-ribbon has had since TASK-733. A corner
/// arrow at the group's bottom right beside its name, which stays centred; named "More {group} options" (or its own
/// label) and tooltipped; a press raises GroupLauncherInvoked and nothing else; a keyboard stop; in a folded group's
/// popup and in the narrow menu.
/// </summary>
public class RibbonLauncherTests
{
    private static int _commandRuns;

    private static RibbonTab Home() => new()
    {
        Id = "home", Label = "Home", Groups = new[]
        {
            new RibbonGroup { Id = "font", Label = "Font", Launcher = true, Items = new[]
            {
                new RibbonItem { Id = "bold", Label = "Bold", Icon = "B", Run = () => _commandRuns++ },
                new RibbonItem { Id = "italic", Label = "Italic", Icon = "I", Run = () => _commandRuns++ },
            } },
            new RibbonGroup { Label = "Paragraph", Launcher = true, LauncherLabel = "Paragraph settings", Items = new[]
            {
                new RibbonItem { Id = "left", Label = "Left", Icon = "⇤" },
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

    private static void Press(Button b)
    {
        ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(b)).Invoke();
        Dispatcher.UIThread.RunJobs();
    }

    private static Rect In(Visual v, Visual frame) => new(v.TranslatePoint(default, frame)!.Value, v.Bounds.Size);

    [AvaloniaFact]
    public void A_launcher_is_named_tooltipped_and_reports_only_itself()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out _);
        var events = new List<RibbonGroupLauncherEventArgs>();
        ribbon.GroupLauncherInvoked += (_, e) => events.Add(e);
        var font = Shown(PanelOf(ribbon), "More Font options");
        RibbonTests.TipText(font).Should().Be("More Font options", "its name is also its tooltip");
        Shown(PanelOf(ribbon), "Paragraph settings").Should().NotBeNull("LauncherLabel names it");
        int runs = _commandRuns;
        Press(font);
        events.Should().ContainSingle().Which.Should().BeEquivalentTo(new { TabId = "home", GroupId = "font" });
        _commandRuns.Should().Be(runs, "a launcher runs no command");
        Press(Shown(PanelOf(ribbon), "Paragraph settings"));
        events[^1].GroupId.Should().Be("Paragraph", "a group without an Id reports its label");
    }

    [AvaloniaFact]
    public void It_sits_at_the_bottom_right_and_the_name_stays_centred()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out _);
        var panel = PanelOf(ribbon);
        var launcher = Shown(panel, "More Font options");
        var group = panel.Children.First(c => c.Bounds.X > -1000 && c.GetVisualDescendants().Contains(launcher));
        var box = In(group, panel);
        var at = In(launcher, panel);
        (box.Right - at.Right).Should().BeLessThan(8, "at the group's right edge (inside its padding)");
        (box.Bottom - at.Bottom).Should().BeLessThan(6, "at its bottom, beside the name");
        var name = In(group.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Font"), panel);
        Math.Abs((name.Left + name.Right) / 2 - (box.Left + box.Right) / 2).Should().BeLessThanOrEqualTo(1, "the name stays centred");
        name.Right.Should().BeLessThanOrEqualTo(at.Left, "and clear of the launcher");
        (panel.Bounds.Height - name.Bottom).Should().BeGreaterThanOrEqualTo(3, "room for the name's descenders inside the row's clip");
    }

    [AvaloniaFact]
    public void It_is_a_keyboard_stop()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out var window);
        var italic = Shown(PanelOf(ribbon), "Italic");
        italic.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        italic.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down, Source = italic });
        Dispatcher.UIThread.RunJobs();
        global::Avalonia.Automation.AutomationProperties.GetName((Control)window.FocusManager!.GetFocusedElement()!)
            .Should().Be("More Font options", "Down from the column's last command reaches the launcher under it");
    }

    [AvaloniaFact]
    public void In_a_folded_group_it_is_in_the_popup_and_closes_it()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() }, PreferredGroupSize = RibbonGroupSize.Popup }, out _);
        int launched = 0;
        ribbon.GroupLauncherInvoked += (_, _) => launched++;
        var chunk = PanelOf(ribbon).GetVisualDescendants().OfType<Button>()
            .First(b => b.GetType().Name == "RibbonChunkButton" && b.IsEffectivelyEnabled && RibbonTests.TipText(b) == "Font");
        var popup = (Flyout)chunk.Flyout!;
        popup.ShowAt(chunk);
        Dispatcher.UIThread.RunJobs();
        Press(Shown((Visual)popup.Content!, "More Font options"));
        launched.Should().Be(1);
        popup.IsOpen.Should().BeFalse("opening the dialog closes the group's popup, as a command does");
    }

    [AvaloniaFact]
    public void The_narrow_menu_offers_it()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() }, NarrowThreshold = 2000 }, out _);
        int launched = 0;
        ribbon.GroupLauncherInvoked += (_, _) => launched++;
        Press(ribbon.GetVisualDescendants().OfType<Button>().First(b => RibbonTests.TipText(b) == "Open the ribbon menu"));
        var entry = ribbon.OpenOverlay!.GetVisualDescendants().OfType<Button>()
            .First(b => global::Avalonia.Automation.AutomationProperties.GetName(b) == "More Font options");
        Press(entry);
        launched.Should().Be(1);
    }

    [AvaloniaFact]
    public void Capture_launcher_screenshot()
    {
        var dir = System.Environment.GetEnvironmentVariable("BIRKO_SHOTS") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "birko-xaml-shots");
        System.IO.Directory.CreateDirectory(dir);
        var ribbon = new Ribbon { Tabs = new[] { Home() }, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Content = ribbon, Width = 420, Height = 160 };
        window.Show();
        window.Measure(new Size(420, 160));
        window.Arrange(new Rect(0, 0, 420, 160));
        Dispatcher.UIThread.RunJobs();
        global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)?.Save(System.IO.Path.Combine(dir, "ribbon-launcher.png"));
        window.Close();
    }
}
