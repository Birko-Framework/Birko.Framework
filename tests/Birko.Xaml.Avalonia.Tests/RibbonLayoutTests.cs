using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Core.Ribbon;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// TASK-751 (FEATURE-019 D1 on the desktop): the layout b-ribbon has had since TASK-730, TASK-743 and TASK-746. Groups
/// are one height, command rows line up across groups, names sit on one line below, each divider is halfway between
/// two groups, a group narrower than its name centres its commands, and a compact folded group without an icon shows
/// its initial. Measured on the arranged tree, as ribbon-layout-smoke measures the web one.
/// </summary>
public class RibbonLayoutTests
{
    // Uneven on purpose: 3, 3, 2 commands with mixed glyphs, the shape that showed the defect on the web.
    private static RibbonTab Home(bool icons = true)
    {
        RibbonItem I(string label, string icon) => new() { Id = label, Label = label, Icon = icon };
        return new RibbonTab
        {
            Id = "home", Label = "Home", Groups = new[]
            {
                new RibbonGroup { Label = "Clipboard", Icon = icons ? "📋" : null, ScalingPriority = 3, Items = new[] { I("Paste", "📋"), I("Cut", "✂"), I("Copy", "📄") } },
                new RibbonGroup { Label = "Records", Icon = icons ? "🗂" : null, Items = new[] { I("New", "➕"), I("Delete", "🗑"), I("Duplicate", "⧉") } },
                new RibbonGroup { Label = "Layout", Icon = icons ? "▦" : null, Items = new[] { I("Columns", "▥"), I("Rows", "▤") } },
            },
        };
    }

    private static Ribbon Show(Ribbon ribbon, double width, double height = 220)
    {
        var window = new Window { Content = ribbon, Width = width, Height = height };
        window.Show();
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        Dispatcher.UIThread.RunJobs();
        return ribbon;
    }

    private static Panel PanelOf(Ribbon ribbon) =>
        (Panel)ribbon.GetVisualDescendants().First(v => v.GetType().Name == "RibbonGroupsPanel");

    private static IReadOnlyList<Control> Dividers(Panel panel) =>
        (IReadOnlyList<Control>)panel.GetType().GetProperty("Dividers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;

    /// <summary>The groups on display, left to right: the panel's arranged children that are not dividers.</summary>
    private static List<Control> ShownGroups(Panel panel)
    {
        var dividers = Dividers(panel);
        return panel.Children.Where(c => !dividers.Contains(c) && c.Bounds.X > -1000).OrderBy(c => c.Bounds.X).ToList();
    }

    private static Rect In(Visual v, Visual panel) =>
        new(v.TranslatePoint(new Point(0, 0), panel)!.Value, v.Bounds.Size);

    private static List<Button> Commands(Control group) =>
        group.GetVisualDescendants().OfType<Button>().ToList();

    private static TextBlock NameOf(Control group, string label) =>
        group.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == label);

    [AvaloniaFact]
    public void Groups_are_one_height_and_their_rows_line_up()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, 1100);
        var panel = PanelOf(ribbon);
        var groups = ShownGroups(panel);
        groups.Should().HaveCount(3);
        groups.Select(g => Math.Round(g.Bounds.Height)).Distinct().Should().HaveCount(1, "every group is arranged at the row's height");
        // The n-th command of each group sits on the same line, within a pixel (glyphs differ by one).
        for (int row = 0; row < 2; row++)
        {
            var tops = groups.Select(g => In(Commands(g)[row], panel).Top).ToList();
            (tops.Max() - tops.Min()).Should().BeLessThanOrEqualTo(1, $"row {row + 1} lines up across groups ({string.Join(", ", tops)})");
        }
    }

    [AvaloniaFact]
    public void Group_names_share_one_line_under_their_commands()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, 1100);
        var panel = PanelOf(ribbon);
        var groups = ShownGroups(panel);
        string[] names = { "Clipboard", "Records", "Layout" };
        var labels = groups.Select((g, i) => In(NameOf(g, names[i]), panel)).ToList();
        labels.Select(l => Math.Round(l.Top)).Distinct().Should().HaveCount(1, "a group with two commands names itself on the same line");
        for (int i = 0; i < groups.Count; i++)
            Commands(groups[i]).Max(b => In(b, panel).Bottom).Should().BeLessThanOrEqualTo(labels[i].Top + 1, $"{names[i]} sits under its commands");
    }

    [AvaloniaFact]
    public void Group_names_are_not_clipped_by_the_row()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() }, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top }, 1100);
        var panel = PanelOf(ribbon);
        var groups = ShownGroups(panel);
        var name = In(NameOf(groups[0], "Clipboard"), panel);
        // The text's ink reaches a few pixels below a line box that is exactly the font size; the row clips at its
        // bounds, so the name needs that room inside them.
        (panel.Bounds.Height - name.Bottom).Should().BeGreaterThanOrEqualTo(3, "room for the descenders inside the clip");
    }

    [AvaloniaTheory]
    [InlineData(1100)]
    [InlineData(520)]
    [InlineData(260)]
    public void Each_divider_is_halfway_between_two_groups(double width)
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, width);
        var panel = PanelOf(ribbon);
        var groups = ShownGroups(panel);
        var dividers = Dividers(panel);
        dividers.Should().HaveCount(groups.Count - 1);
        for (int i = 1; i < groups.Count; i++)
        {
            var line = dividers[i - 1].Bounds;
            double before = line.Left - groups[i - 1].Bounds.Right;
            double after = groups[i].Bounds.Left - line.Right;
            Math.Abs(before - after).Should().BeLessThanOrEqualTo(1, $"at {width}px divider {i} sits halfway ({before} / {after})");
            line.Height.Should().BeApproximately(panel.Bounds.Height, 0.5, "it runs the row's full height");
        }
        dividers.Should().OnlyContain(d => !d.IsHitTestVisible && !d.Focusable, "a divider is not a stop or a target");
    }

    [AvaloniaFact]
    public void A_group_narrower_than_its_name_centres_its_commands()
    {
        // Small: icons only, so "Clipboard" is wider than its three-icon column.
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() }, PreferredGroupSize = RibbonGroupSize.Small }, 1100);
        var panel = PanelOf(ribbon);
        foreach (var group in ShownGroups(panel))
        {
            var box = In(group, panel);
            var cmds = Commands(group).Select(b => In(b, panel)).ToList();
            double left = cmds.Min(c => c.Left) - box.Left, right = box.Right - cmds.Max(c => c.Right);
            Math.Abs(left - right).Should().BeLessThanOrEqualTo(1, $"commands centred in their group ({left} / {right})");
        }
    }

    [AvaloniaFact]
    public void A_compact_folded_group_without_an_icon_shows_its_initial()
    {
        // Narrow switch off: below its threshold the ribbon becomes a menu and has no groups row at all.
        var ribbon = Show(new Ribbon { Tabs = new[] { Home(icons: false) }, NarrowThreshold = 0 }, 140);
        var panel = PanelOf(ribbon);
        var shown = ShownGroups(panel);
        var texts = shown.Select(g => string.Concat(g.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text))).ToList();
        texts.Should().Equal(new[] { "C", "R", "L" }, "compact chunks show the group's initial, not one generic glyph");
        shown.Should().OnlyContain(g => g.Bounds.Width > 8 && g.Bounds.Height > 16, "something visible to press");
        // Labelled, an icon-less chunk shows its name and no stand-in glyph.
        var labelled = Show(new Ribbon { Tabs = new[] { Home(icons: false) }, PreferredGroupSize = RibbonGroupSize.Popup }, 1100);
        ShownGroups(PanelOf(labelled)).SelectMany(g => g.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text))
            .Should().NotContain("▦").And.Contain("Clipboard ⌄");
    }

    [AvaloniaFact]
    public void Capture_layout_screenshots()
    {
        var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS") ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
        Directory.CreateDirectory(dir);
        foreach (var (theme, name) in new[] { (BirkoThemeVariants.Light, "light"), (BirkoThemeVariants.Dark, "dark") })
        {
            Application.Current!.RequestedThemeVariant = theme;
            foreach (double width in new[] { 1100.0, 520, 260 })
            {
                // Top-aligned, as an app docks it: the ribbon takes its own height, not the window's.
                var ribbon = new Ribbon { Tabs = new[] { Home(), new RibbonTab { Id = "view", Label = "View" } }, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top };
                var window = new Window { Content = ribbon, Width = width, Height = 220 };
                window.Show();
                window.Measure(new Size(width, 220));
                window.Arrange(new Rect(0, 0, width, 220));
                Dispatcher.UIThread.RunJobs();
                global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)?
                    .Save(Path.Combine(dir, $"ribbon-layout-{name}-{width}.png"));
                window.Close();
            }
        }
        Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light;
        // Compact folded groups without icons: their initials.
        var compact = new Ribbon { Tabs = new[] { Home(icons: false) }, NarrowThreshold = 0, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top };
        var w = new Window { Content = compact, Width = 140, Height = 160 };
        w.Show();
        w.Measure(new Size(140, 160));
        w.Arrange(new Rect(0, 0, 140, 160));
        Dispatcher.UIThread.RunJobs();
        global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(w)?.Save(Path.Combine(dir, "ribbon-layout-initials.png"));
        w.Close();
    }
}
