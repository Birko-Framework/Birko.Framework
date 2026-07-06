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

    private static T Show<T>(T control) where T : Control
    {
        var window = new Window { Content = control, Width = 700, Height = 200 };
        window.Show();
        window.Measure(new Size(700, 200));
        window.Arrange(new Rect(0, 0, 700, 200));
        Dispatcher.UIThread.RunJobs();
        return control;
    }

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
