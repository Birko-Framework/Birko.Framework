using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
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
/// TASK-754 (FEATURE-019 D10 on the desktop): toggle buttons and one-of sets, as b-ribbon has had since TASK-739. A
/// click flips a toggle (or picks a set member and releases the others); the pressed look is the primary tint with a
/// 1px primary outline, the same size off and on; SetPressed is silent and IsPressed reads; every copy keeps in step;
/// automation reports a toggle or a radio button.
/// </summary>
public class RibbonToggleTests
{
    private static int _ran;

    private static RibbonTab Home(string id = "home") => new()
    {
        Id = id, Label = "Home", Groups = new[]
        {
            new RibbonGroup { Label = "Font", ScalingPriority = 5, Items = new[]
            {
                new RibbonItem { Id = "bold", Label = "Bold", Icon = "B", Toggle = true, Pressed = true, Run = () => _ran++ },
                new RibbonItem { Id = "italic", Label = "Italic", Icon = "I", Toggle = true },
                new RibbonItem { Id = "colour", Label = "Colour", Icon = "●" },
            } },
            new RibbonGroup { Label = "Layout", Items = new[]
            {
                new RibbonItem { Id = "cols", Label = "Columns", Icon = "▥", ToggleGroup = "orient", Pressed = true },
                new RibbonItem { Id = "rows", Label = "Rows", Icon = "▤", ToggleGroup = "orient" },
            } },
        },
    };

    private static Ribbon Show(Ribbon ribbon, out Window window, double width = 1000)
    {
        window = new Window { Content = ribbon, Width = width, Height = 240 };
        window.Show();
        Layout(window, width);
        return ribbon;
    }

    private static void Layout(Window window, double width)
    {
        window.Width = width;
        window.Measure(new Size(width, 240));
        window.Arrange(new Rect(0, 0, width, 240));
        Dispatcher.UIThread.RunJobs();
    }

    private static Panel PanelOf(Ribbon ribbon) =>
        (Panel)ribbon.GetVisualDescendants().First(v => v.GetType().Name == "RibbonGroupsPanel");

    private static List<Button> All(Visual scope, string name) =>
        scope.GetVisualDescendants().OfType<Button>()
            .Where(b => global::Avalonia.Automation.AutomationProperties.GetName(b) == name).ToList();

    private static Button Shown(Ribbon ribbon, string name) =>
        All(PanelOf(ribbon), name).First(b => b.IsEffectivelyEnabled && b.TranslatePoint(default, PanelOf(ribbon)) is { X: > -1000 });

    private static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static IBrush Brush(Control c, string key) =>
        c.TryFindResource(key, c.ActualThemeVariant, out var v) && v is IBrush b ? b : throw new System.Exception(key);

    private static bool LooksPressed(Button b) =>
        ReferenceEquals(b.Background, Brush(b, "BColorPrimaryLightBrush"))
        && b.Content is Border { BorderBrush: var frame } && ReferenceEquals(frame, Brush(b, "BColorPrimaryBrush"));

    private static bool LooksOff(Button b) =>
        ReferenceEquals(b.Background, Brushes.Transparent) && b.Content is Border { BorderBrush: var frame } && ReferenceEquals(frame, Brushes.Transparent);

    [AvaloniaFact]
    public void A_toggle_flips_reports_and_runs_with_the_picked_look_and_no_size_change()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out _);
        var events = new List<RibbonItemToggledEventArgs>();
        ribbon.ItemToggled += (_, e) => events.Add(e);
        var bold = Shown(ribbon, "Bold");
        LooksPressed(bold).Should().BeTrue("Pressed = true is the initial state");
        var size = bold.Bounds.Size;
        int ran = _ran;

        Click(bold);
        ribbon.IsPressed("bold").Should().BeFalse();
        LooksOff(bold).Should().BeTrue();
        events.Should().ContainSingle().Which.Should().BeEquivalentTo(new { TabId = "home", ItemId = "bold", Pressed = false });
        _ran.Should().Be(ran + 1, "the command still runs");
        Layout((Window)TopLevel.GetTopLevel(ribbon)!, 1000);
        bold.Bounds.Size.Should().Be(size, "pressing never resizes the button");

        Click(bold);
        LooksPressed(bold).Should().BeTrue();
        events[^1].Pressed.Should().BeTrue();
    }

    [AvaloniaFact]
    public void A_one_of_set_keeps_exactly_one_on()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out _);
        var events = new List<RibbonItemToggledEventArgs>();
        ribbon.ItemToggled += (_, e) => events.Add(e);
        Click(Shown(ribbon, "Rows"));
        ribbon.IsPressed("rows").Should().BeTrue();
        ribbon.IsPressed("cols").Should().BeFalse("pressing one releases the others");
        LooksOff(Shown(ribbon, "Columns")).Should().BeTrue();
        Click(Shown(ribbon, "Rows"));
        ribbon.IsPressed("rows").Should().BeTrue("pressing the one that is on keeps it on");
        events.Select(e => e.Pressed).Should().Equal(true, true);
    }

    [AvaloniaFact]
    public void SetPressed_is_silent_and_every_copy_keeps_in_step()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() }, NarrowThreshold = 0 }, out var window);
        int events = 0;
        ribbon.ItemToggled += (_, _) => events++;
        ribbon.SetPressed("italic", true);
        events.Should().Be(0, "SetPressed raises no event");
        ribbon.IsPressed("italic").Should().BeTrue();
        All(PanelOf(ribbon), "Italic").Should().HaveCountGreaterThan(1, "precondition: a copy per size")
            .And.OnlyContain(b => LooksPressed(b), "every copy, parked ones too");
        ribbon.SetPressed("rows", true);
        ribbon.IsPressed("cols").Should().BeFalse("turning a member on releases the others, from the app too");

        Layout(window, 180);
        ribbon.ResolvedGroupSizes.Should().Contain(s => s != RibbonGroupSize.Medium, "precondition: the row rescaled");
        LooksPressed(Shown(ribbon, "Italic")).Should().BeTrue("the state survives a rescale");

        ribbon.Tabs = new[] { Home() };
        Dispatcher.UIThread.RunJobs();
        ribbon.IsPressed("italic").Should().BeFalse("new tabs: their Pressed values are the state again");
        ribbon.IsPressed("cols").Should().BeTrue();
    }

    [AvaloniaFact]
    public void Automation_reports_a_toggle_and_a_radio_button()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out _);
        var boldPeer = ControlAutomationPeer.CreatePeerForElement(Shown(ribbon, "Bold"));
        boldPeer.Should().BeAssignableTo<IToggleProvider>().Which.ToggleState.Should().Be(ToggleState.On);
        var colsPeer = ControlAutomationPeer.CreatePeerForElement(Shown(ribbon, "Columns"));
        colsPeer.GetAutomationControlType().Should().Be(AutomationControlType.RadioButton);
        colsPeer.Should().BeAssignableTo<ISelectionItemProvider>().Which.IsSelected.Should().BeTrue();
        ((ISelectionItemProvider)ControlAutomationPeer.CreatePeerForElement(Shown(ribbon, "Rows"))).IsSelected.Should().BeFalse();
        var plain = ControlAutomationPeer.CreatePeerForElement(Shown(ribbon, "Colour"));
        plain.Should().NotBeAssignableTo<IToggleProvider>("a plain command has no state");
    }

    [AvaloniaFact]
    public void The_look_follows_a_theme_switch()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() } }, out _);
        var bold = Shown(ribbon, "Bold");
        Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Dark;
        Dispatcher.UIThread.RunJobs();
        try
        {
            LooksPressed(bold).Should().BeTrue("the pressed brushes are the dark theme's now");
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light;
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void Capture_toggle_screenshots()
    {
        var dir = System.Environment.GetEnvironmentVariable("BIRKO_SHOTS") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "birko-xaml-shots");
        System.IO.Directory.CreateDirectory(dir);
        foreach (var (theme, name) in new[] { (BirkoThemeVariants.Light, "light"), (BirkoThemeVariants.Dark, "dark") })
        {
            Application.Current!.RequestedThemeVariant = theme;
            var ribbon = new Ribbon { Tabs = new[] { Home() }, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top };
            var window = new Window { Content = ribbon, Width = 520, Height = 160 };
            window.Show();
            Layout(window, 520);
            global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)?.Save(System.IO.Path.Combine(dir, $"ribbon-toggles-{name}.png"));
            window.Close();
        }
        Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light;
    }

    [AvaloniaFact]
    public void The_narrow_menu_keeps_the_state()
    {
        var ribbon = Show(new Ribbon { Tabs = new[] { Home() }, NarrowThreshold = 2000 }, out _);
        var burger = ribbon.GetVisualDescendants().OfType<Button>().First(b => RibbonTests.TipText(b) == "Open the ribbon menu");
        Click(burger);
        var overlay = ribbon.OpenOverlay;
        overlay.Should().NotBeNull("precondition: the narrow menu is open");
        var bold = overlay!.GetVisualDescendants().OfType<Button>().First(b => global::Avalonia.Automation.AutomationProperties.GetName(b) == "Bold");
        LooksPressed(bold).Should().BeTrue();
        Click(bold);
        ribbon.IsPressed("bold").Should().BeFalse("a press there is a press");
    }
}
