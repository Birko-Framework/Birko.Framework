using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Core.Ribbon;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// TASK-122 (FEATURE-015 D13 / D14, STORY-056): a group with mixed command sizes, Word's Clipboard (a big Paste beside
/// Cut, Copy and Format Painter stacked), drawn on the desktop as b-ribbon draws it, checked by its own
/// <c>ribbon-mixed-check</c>: the layout, the arrow keys in visual order, names at every step, and the collapse to a
/// lossless Popup.
/// </summary>
public class RibbonMixedSizeTests
{
    private static RibbonTab Home()
    {
        RibbonItem I(string label, RibbonGroupSize? size = null) => new() { Id = label, Label = label, Icon = "●", Size = size };
        return new RibbonTab
        {
            Id = "home", Label = "Home", Groups = new[]
            {
                new RibbonGroup { Label = "Clipboard", ScalingPriority = 0, Items = new[] { I("Paste", RibbonGroupSize.Large), I("Cut"), I("Copy"), I("Format Painter") } },
                new RibbonGroup { Label = "Font", ScalingPriority = 10, Items = new[] { I("Bold"), I("Italic"), I("Underline"), I("Grow Font"), I("Shrink Font"), I("Clear") } },
            },
        };
    }

    private static Ribbon Show(out Window window, double width)
    {
        var ribbon = new Ribbon { Tabs = new[] { Home() } };
        window = new Window { Content = ribbon, Width = width, Height = 300 };
        window.Show();
        Layout(window, width);
        return ribbon;
    }

    private static void Layout(Window window, double width)
    {
        window.Width = width;
        window.Measure(new Size(width, 300));
        window.Arrange(new Rect(0, 0, width, 300));
        for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
    }

    private static Panel PanelOf(Ribbon ribbon) =>
        (Panel)ribbon.GetVisualDescendants().First(v => v.GetType().Name == "RibbonGroupsPanel");

    private static string? Name(Control c) => global::Avalonia.Automation.AutomationProperties.GetName(c);

    /// <summary>The on-screen commands (parked variants sit far off to the left).</summary>
    private static List<Button> Stops(Panel panel) =>
        panel.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyEnabled && b.TranslatePoint(default, panel) is { X: > -1000 }).ToList();

    private static Button Command(Panel panel, string name) => Stops(panel).First(b => Name(b) == name);

    private static Rect RectIn(Control c, Panel panel) => new(c.TranslatePoint(default, panel)!.Value, c.Bounds.Size);

    private static void Key_(Window window, Key key)
    {
        var target = (Control)window.FocusManager!.GetFocusedElement()!;
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = target });
        Dispatcher.UIThread.RunJobs();
    }

    private static string? Focused(Window window) => window.FocusManager?.GetFocusedElement() is Control c ? Name(c) : null;

    [AvaloniaFact]
    public void Wide_Paste_spans_the_rows_and_the_rest_stack_beside_it()
    {
        var panel = PanelOf(Show(out _, 1000));
        var paste = RectIn(Command(panel, "Paste"), panel);
        var cut = RectIn(Command(panel, "Cut"), panel);
        var copy = RectIn(Command(panel, "Copy"), panel);
        var format = RectIn(Command(panel, "Format Painter"), panel);

        paste.Height.Should().BeGreaterThan(cut.Height * 2, "Paste is Large, one column across the rows");
        cut.X.Should().BeGreaterThan(paste.Right - 1, "the stack is right of Paste");
        copy.X.Should().BeApproximately(cut.X, 1);
        format.X.Should().BeApproximately(cut.X, 1);
        cut.Y.Should().BeLessThan(copy.Y);
        copy.Y.Should().BeLessThan(format.Y);
        Command(panel, "Paste").Classes.Should().Contain("mixed-large");
    }

    [AvaloniaFact]
    public void A_uniform_group_beside_it_is_drawn_as_before()
    {
        var panel = PanelOf(Show(out _, 1000));

        new[] { "Bold", "Italic", "Underline", "Grow Font" }.Select(n => Command(panel, n).Classes.Contains("mixed-large"))
            .Should().AllBeEquivalentTo(false);
        var bold = RectIn(Command(panel, "Bold"), panel);
        var italic = RectIn(Command(panel, "Italic"), panel);
        italic.X.Should().BeApproximately(bold.X, 1, "Medium: three stacked per column, as before");
        italic.Y.Should().BeGreaterThan(bold.Y);
    }

    [AvaloniaFact]
    public void The_arrows_walk_the_mixed_group_in_visual_order()
    {
        var panel = PanelOf(Show(out var window, 1000));
        Command(panel, "Paste").Focus(NavigationMethod.Tab).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();

        var visited = new List<string?> { Focused(window) };
        foreach (var key in new[] { Key.Right, Key.Down, Key.Down, Key.Left })
        {
            Key_(window, key);
            visited.Add(Focused(window));
        }

        string.Join(" > ", visited).Should().Be("Paste > Cut > Copy > Format Painter > Paste",
            "Right from the big Paste goes to the top of the next column, as in Office and b-ribbon");
    }

    [AvaloniaFact]
    public void Every_command_keeps_its_name_at_every_width_and_the_group_collapses_losslessly()
    {
        var ribbon = Show(out var window, 1000);
        bool sawPopup = false;
        for (double w = 1000; w >= 260; w -= 20)
        {
            Layout(window, w);
            var panel = PanelOf(ribbon);
            var chunk = Stops(panel).FirstOrDefault(b => b.GetType().Name == "RibbonChunkButton" && Name(b) == "Clipboard");
            if (chunk is null)
            {
                foreach (var n in new[] { "Paste", "Cut", "Copy", "Format Painter" })
                    Stops(panel).Count(b => Name(b) == n).Should().Be(1, $"'{n}' is on screen and named at {w}px");
                continue;
            }
            sawPopup = true;
            var content = ((Flyout)chunk.Flyout!).Content as Control;
            content.Should().NotBeNull();
            content!.GetLogicalDescendants().OfType<Button>().Select(Name)
                .Should().Contain(new[] { "Paste", "Cut", "Copy", "Format Painter" }, $"the flyout holds every command at {w}px");
            break;
        }
        sawPopup.Should().BeTrue("the mixed group collapses to Popup when the row is narrow enough");
    }

    [AvaloniaFact]
    public void No_variant_is_hidden_from_measurement()
    {
        var panel = PanelOf(Show(out _, 1000));

        // Parked off-screen rather than IsVisible = false: a hidden control measures as zero (STORY-049).
        panel.Children.Where(c => c.GetVisualDescendants().OfType<Button>().Any(b => Name(b) == "Cut"))
            .Should().NotBeEmpty().And.OnlyContain(c => c.IsVisible);
    }

    [AvaloniaFact]
    public void At_the_Small_step_the_icons_sit_at_the_column_start_under_Paste()
    {
        // Seen side by side with b-ribbon (2026-11-25): stretched to the column's width, a Small command centred its icon
        // under the wider "Paste ▾"; the web's justify-items: start puts it under Paste's icon, as Office does.
        var ribbon = Show(out var window, 1000);
        for (double w = 1000; w >= 260; w -= 20)
        {
            Layout(window, w);
            var panel = PanelOf(ribbon);
            var pasteBtn = Stops(panel).FirstOrDefault(b => Name(b) == "Paste");
            var cutBtn = Stops(panel).FirstOrDefault(b => Name(b) == "Cut");
            if (pasteBtn is null || cutBtn is null) break;
            var paste = RectIn(pasteBtn, panel);
            var cut = RectIn(cutBtn, panel);
            if (cut.Y < paste.Bottom - 1) continue; // Paste still spans the rows, the stack is beside it
            // The ICONS, not the buttons: a stretched button starts at the column's edge and centres its icon inside it.
            Rect Icon(Button b) => RectIn(b.GetVisualDescendants().OfType<TextBlock>().First(), panel);
            var pasteIcon = Icon(pasteBtn);
            Icon(cutBtn).X.Should().BeApproximately(pasteIcon.X, 2, $"Cut's icon is under Paste's at {w}px");
            Icon(Command(panel, "Copy")).X.Should().BeApproximately(pasteIcon.X, 2, $"Copy's icon is under Paste's at {w}px");
            return;
        }
        throw new Xunit.Sdk.XunitException("the Clipboard group never reached the step where Paste shares a column with the icons");
    }
}
