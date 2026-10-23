using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Core.Ribbon;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// TASK-760 (FEATURE-019 D5 on the desktop): contextual tabs, as b-ribbon has had since TASK-734. A set's tabs stay
/// hidden until the app shows it, then come after the normal tabs inside the set's band, pill or edge in its theme
/// colour; hiding the open set goes back to the normal tab that was open before; each tab names its set to assistive
/// technology; the narrow menu lists only the shown tabs.
/// </summary>
public class RibbonContextualTests
{
    private static RibbonTab Tab(string id, string label, string? set = null) => new()
    {
        Id = id, Label = label, ContextualSet = set,
        Groups = new[] { new RibbonGroup { Label = label + " group", Items = new[] { new RibbonItem { Id = id + "-cmd", Label = label + " command" } } } },
    };

    private static Ribbon Build() => new()
    {
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        NarrowThreshold = 0,
        Tabs = new[] { Tab("home", "Home"), Tab("design", "Table Design", "table"), Tab("layout", "Layout", "table"), Tab("view", "View"), Tab("format", "Picture Format", "picture") },
    };

    private static readonly RibbonContextualSet[] Sets =
    {
        new() { Id = "table", Label = "Table Tools", Color = RibbonContextualColor.Success },
        new() { Id = "picture", Label = "Picture Tools", Color = RibbonContextualColor.Warning, Style = RibbonContextualStyle.Pill },
    };

    private static Ribbon Show(Ribbon ribbon, out Window window, double width = 1000)
    {
        window = new Window { Content = ribbon, Width = width, Height = 240 };
        window.Show();
        Settle(window, width);
        return ribbon;
    }

    private static void Settle(Window window, double width = 1000)
    {
        window.Measure(new Size(width, 240));
        window.Arrange(new Rect(0, 0, width, 240));
        for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
    }

    private static string? Name(Control c) => global::Avalonia.Automation.AutomationProperties.GetName(c);

    private static Button TabButton(Ribbon ribbon, string label) =>
        ribbon.GetVisualDescendants().OfType<Button>().First(b => Name(b) == label);

    private static List<string?> TabNames(Ribbon ribbon) =>
        ribbon.GetVisualDescendants().OfType<Button>().Select(Name)
            .Where(n => n is "Home" or "Table Design" or "Layout" or "View" or "Picture Format").ToList();

    private static Border Mark(Ribbon ribbon, string set) =>
        ribbon.GetVisualDescendants().OfType<Border>().Single(b => b.Tag as string == $"ctx:{set}");

    private static Color Resource(Control c, string key) =>
        c.TryFindResource(key, c.ActualThemeVariant, out var v) && v is ISolidColorBrush b ? b.Color : default;

    [AvaloniaFact]
    public void A_sets_tabs_are_hidden_until_shown_and_come_after_the_normal_tabs()
    {
        var ribbon = Show(Build(), out var window);
        ribbon.SetContextualSets(Sets);
        Settle(window);
        TabNames(ribbon).Should().Equal("Home", "View");

        ribbon.ShowContextual("table");
        Settle(window);
        ribbon.IsContextualShown("table").Should().BeTrue();
        TabNames(ribbon).Should().Equal("Home", "View", "Table Design", "Layout");
        ribbon.SelectedIndex.Should().Be(0, "showing a set does not move the open tab");

        ribbon.ShowContextual("picture");
        Settle(window);
        TabNames(ribbon).Should().Equal(new[] { "Home", "View", "Table Design", "Layout", "Picture Format" }, "shown sets follow in declaration order");
    }

    [AvaloniaFact]
    public void A_tab_of_an_undeclared_set_never_shows()
    {
        var ribbon = Show(Build(), out var window);
        ribbon.ShowContextual("table");
        Settle(window);
        TabNames(ribbon).Should().Equal("Home", "View");
    }

    [AvaloniaFact]
    public void Show_with_select_opens_the_first_tab_and_hide_goes_back_to_the_previous_normal_tab()
    {
        var ribbon = Show(Build(), out var window);
        ribbon.SetContextualSets(Sets);
        ribbon.SelectedIndex = 1; // View
        Settle(window);
        ribbon.ShowContextual("table", select: true);
        Settle(window);
        ribbon.SelectedIndex.Should().Be(2);
        ribbon.GetVisualDescendants().OfType<Button>().Should().Contain(b => Name(b) == "Table Design command", "its groups are open");

        TabButton(ribbon, "Layout").Focus();
        ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(TabButton(ribbon, "Layout"))).Invoke();
        Settle(window);
        ribbon.SelectedIndex.Should().Be(3);

        ribbon.HideContextual("table");
        Settle(window);
        ribbon.SelectedIndex.Should().Be(1, "back to View, the normal tab open before the set");
        ribbon.GetVisualDescendants().OfType<Button>().Should().Contain(b => Name(b) == "View command");
    }

    [AvaloniaFact]
    public void Hiding_a_set_before_the_open_one_keeps_the_open_tab_open()
    {
        var ribbon = Show(Build(), out var window);
        ribbon.SetContextualSets(Sets);
        ribbon.ShowContextual("table");
        ribbon.ShowContextual("picture", select: true);
        Settle(window);
        ribbon.SelectedIndex.Should().Be(4);
        ribbon.HideContextual("table");
        Settle(window);
        ribbon.SelectedIndex.Should().Be(2, "Picture Format moved left and stays open");
        ribbon.GetVisualDescendants().OfType<Button>().Should().Contain(b => Name(b) == "Picture Format command");
    }

    [AvaloniaTheory]
    [InlineData(RibbonContextualStyle.Band)]
    [InlineData(RibbonContextualStyle.Pill)]
    [InlineData(RibbonContextualStyle.Edge)]
    public void The_three_looks_mark_the_set_in_its_theme_colour(RibbonContextualStyle style)
    {
        var ribbon = Show(Build(), out var window);
        ribbon.ContextualStyle = style;
        ribbon.SetContextualSets(new[] { new RibbonContextualSet { Id = "table", Label = "Table Tools", Color = RibbonContextualColor.Success } });
        ribbon.ShowContextual("table", select: true);
        Settle(window);
        var mark = Mark(ribbon, "table");
        var names = mark.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == "Table Tools").ToList();
        if (style == RibbonContextualStyle.Edge) names.Should().BeEmpty("the edge is a line without the name");
        else names.Should().ContainSingle().Which.Foreground.Should().BeAssignableTo<ISolidColorBrush>()
            .Which.Color.Should().Be(Resource(mark, "BColorSuccessTextBrush"));
        if (style != RibbonContextualStyle.Pill)
        {
            mark.BorderThickness.Top.Should().Be(3, "the accent line, a token");
            ((ISolidColorBrush)mark.BorderBrush!).Color.Should().Be(Resource(mark, "BColorSuccessBrush"));
        }
        if (style == RibbonContextualStyle.Band)
            ((ISolidColorBrush)mark.Background!).Color.Should().Be(Resource(mark, "BColorSuccessAlphaBgBrush"), "a tinted band");
        var open = TabButton(ribbon, "Table Design");
        ((ISolidColorBrush)open.Foreground!).Color.Should().Be(Resource(mark, "BColorSuccessTextBrush"));
        open.BorderThickness.Bottom.Should().Be(2, "the open contextual tab is underlined");
    }

    [AvaloniaFact]
    public void A_set_can_override_the_ribbons_look()
    {
        var ribbon = Show(Build(), out var window);
        ribbon.SetContextualSets(Sets);
        ribbon.ShowContextual("picture");
        Settle(window);
        Mark(ribbon, "picture").GetVisualDescendants().OfType<Border>()
            .Should().Contain(b => b.CornerRadius.TopLeft > 100, "the picture set asked for a pill");
    }

    [AvaloniaFact]
    public void Each_contextual_tab_names_its_set_for_assistive_technology()
    {
        var ribbon = Show(Build(), out var window);
        ribbon.SetContextualSets(Sets);
        ribbon.ShowContextual("table");
        Settle(window);
        var tab = TabButton(ribbon, "Layout");
        ControlAutomationPeer.CreatePeerForElement(tab).GetHelpText().Should().Be("Table Tools");
        ControlAutomationPeer.CreatePeerForElement(TabButton(ribbon, "Home")).GetHelpText().Should().BeNullOrEmpty();
        var name = Mark(ribbon, "table").GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Table Tools");
        global::Avalonia.Automation.AutomationProperties.GetAccessibilityView(name).Should().Be(global::Avalonia.Automation.AccessibilityView.Raw, "the band's name is decoration");
    }

    [AvaloniaFact]
    public void Arrows_and_key_tips_reach_the_shown_contextual_tabs()
    {
        var ribbon = Show(Build(), out var window);
        ribbon.SetContextualSets(Sets);
        ribbon.ShowContextual("table");
        ribbon.SelectedIndex = 1;
        Settle(window);
        TabButton(ribbon, "View").Focus(NavigationMethod.Tab);
        Settle(window);
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        Settle(window);
        ribbon.SelectedIndex.Should().Be(2, "Right moves from View into the set");

        window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Settle(window);
        var tips = (IReadOnlyDictionary<string, Control>)typeof(Ribbon).GetProperty("KeyTips",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ribbon)!;
        tips.Values.Select(Name).Should().BeEquivalentTo(new[] { "Home", "View", "Table Design", "Layout" });
    }

    [AvaloniaFact]
    public void The_narrow_menu_lists_only_the_shown_tabs()
    {
        var ribbon = Build();
        ribbon.NarrowThreshold = 600;
        Show(ribbon, out var window, 400);
        ribbon.SetContextualSets(Sets);
        Settle(window, 400);
        var burger = ribbon.GetVisualDescendants().OfType<Button>().First(b => Name(b) == "Open the ribbon menu");
        ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(burger)).Invoke();
        Settle(window, 400);
        var headings = ribbon.OpenOverlay!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        headings.Should().Contain("Home").And.NotContain("Table Design");
        ribbon.CloseOverlay();

        ribbon.ShowContextual("table");
        Settle(window, 400);
        burger = ribbon.GetVisualDescendants().OfType<Button>().First(b => Name(b) == "Open the ribbon menu");
        ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(burger)).Invoke();
        Settle(window, 400);
        ribbon.OpenOverlay!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain("Table Design");
    }

    [AvaloniaFact]
    public void Capture_contextual_screenshots()
    {
        var dir = System.Environment.GetEnvironmentVariable("BIRKO_SHOTS") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "birko-xaml-shots");
        System.IO.Directory.CreateDirectory(dir);
        var themes = new[] { ("light", BirkoThemeVariants.Light), ("dark", BirkoThemeVariants.Dark), ("neon", BirkoThemeVariants.Neon), ("finstat", BirkoThemeVariants.Finstat) };
        try
        {
        foreach (var (theme, variant) in themes)
            foreach (var style in new[] { RibbonContextualStyle.Band, RibbonContextualStyle.Pill, RibbonContextualStyle.Edge })
            {
                Application.Current!.RequestedThemeVariant = variant;
                var ribbon = Build();
                ribbon.ContextualStyle = style;
                Show(ribbon, out var window);
                ribbon.SetContextualSets(new[] { new RibbonContextualSet { Id = "table", Label = "Table Tools", Color = RibbonContextualColor.Success }, new RibbonContextualSet { Id = "picture", Label = "Picture Tools", Color = RibbonContextualColor.Warning } });
                ribbon.ShowContextual("table", select: true);
                ribbon.ShowContextual("picture");
                Settle(window);
                window.CaptureRenderedFrame()?.Save(System.IO.Path.Combine(dir, $"ribbon-ctx-{style.ToString().ToLowerInvariant()}-{theme}.png"));
                window.Close();
            }
        }
        finally { Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light; }
    }
}
