using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
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
/// TASK-758 (FEATURE-019 D7 on the desktop): key tips, as b-ribbon has had since TASK-736, started by tapping plain Alt
/// as in desktop Office (the user's choice). Letters on tabs, then commands, then a folded group's popup or a menu;
/// declared letters win; Escape steps back; a click, a letter with no tip or Alt in a text box do what they should.
/// Driven by real headless key presses and releases, so "tap and release Alt with nothing in between" is the input
/// system's own sequence.
/// </summary>
public class RibbonKeyTipTests
{
    private static readonly List<string> Ran = new();

    private static RibbonTab Home() => new()
    {
        Id = "home", Label = "Home", Groups = new[]
        {
            new RibbonGroup { Label = "Clipboard", Icon = "📋", Items = new[]
            {
                new RibbonItem { Id = "paste", Label = "Paste", Icon = "📋", Run = () => Ran.Add("paste") },
                new RibbonItem { Id = "cut", Label = "Cut", Icon = "✂", Keytip = "X", Run = () => Ran.Add("cut") },
            } },
            new RibbonGroup { Label = "Export", Icon = "📤", Items = new[]
            {
                new RibbonItem { Id = "csv", Label = "CSV", Icon = "📊", Menu = new RibbonMenuEntry[]
                {
                    new RibbonMenuItem { Id = "comma", Label = "Comma separated", Run = () => Ran.Add("comma") },
                    new RibbonMenuItem { Id = "semi", Label = "Semicolon separated", Keytip = "S", Run = () => Ran.Add("semi") },
                } },
            } },
        },
    };

    private static Ribbon Build(RibbonGroupSize size = RibbonGroupSize.Medium) => new()
    {
        PreferredGroupSize = size,
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        Tabs = new[] { Home(), new RibbonTab { Id = "view", Label = "View", Groups = new[] { new RibbonGroup { Label = "Zoom", Items = new[] { new RibbonItem { Id = "zoom", Label = "Zoom In", Run = () => Ran.Add("zoom") } } } } } },
    };

    private static Ribbon Show(Ribbon ribbon, out Window window, Control? extra = null)
    {
        var page = new StackPanel();
        page.Children.Add(ribbon);
        if (extra is not null) page.Children.Add(extra);
        window = new Window { Content = page, Width = 900, Height = 260 };
        window.Show();
        window.Measure(new Size(900, 260));
        window.Arrange(new Rect(0, 0, 900, 260));
        Dispatcher.UIThread.RunJobs();
        return ribbon;
    }

    private static void Settle()
    {
        for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void TapAlt(TopLevel target)
    {
        target.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        target.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Settle();
    }

    private static void Letter(TopLevel target, char c)
    {
        var key = (Key)((int)Key.A + (char.ToUpperInvariant(c) - 'A'));
        target.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, c.ToString());
        target.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, c.ToString());
        Settle();
    }

    private static void Escape(TopLevel target)
    {
        target.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        target.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Settle();
    }

    private static Dictionary<string, string> Tips(Ribbon ribbon)
    {
        var map = (IReadOnlyDictionary<string, Control>)typeof(Ribbon).GetProperty("KeyTips",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ribbon)!;
        return map.ToDictionary(p => p.Key, p => p.Value is MenuItem mi ? mi.Header as string ?? ""
            : global::Avalonia.Automation.AutomationProperties.GetName(p.Value) ?? "");
    }

    [AvaloniaFact]
    public void Tapping_Alt_shows_a_letter_on_every_tab()
    {
        var ribbon = Show(Build(), out var window);
        TapAlt(window);
        ribbon.IsKeyTipMode.Should().BeTrue();
        Tips(ribbon).Should().BeEquivalentTo(new Dictionary<string, string> { ["H"] = "Home", ["V"] = "View" });
        var tab = ribbon.GetVisualDescendants().OfType<Button>().First(b => global::Avalonia.Automation.AutomationProperties.GetName(b) == "Home");
        AdornerLayer.GetAdornerLayer(tab)!.Children.OfType<Border>().Select(b => (b.Child as TextBlock)?.Text)
            .Should().Contain("H", "the letter is drawn on the tab");
        TapAlt(window);
        ribbon.IsKeyTipMode.Should().BeFalse("a second tap ends it");
    }

    [AvaloniaFact]
    public void A_tab_letter_shows_its_commands_and_a_command_letter_runs_it()
    {
        Ran.Clear();
        var ribbon = Show(Build(), out var window);
        TapAlt(window);
        Letter(window, 'v');
        ribbon.SelectedIndex.Should().Be(1, "V opens the View tab");
        Tips(ribbon).Values.Should().Contain("Zoom In");
        Letter(window, 'z');
        Ran.Should().Equal("zoom");
        ribbon.IsKeyTipMode.Should().BeFalse("running a command ends the mode");
    }

    [AvaloniaFact]
    public void Letters_are_declared_or_the_first_free_letter_of_the_label()
    {
        var ribbon = Show(Build(), out var window);
        TapAlt(window);
        Letter(window, 'h');
        var tips = Tips(ribbon);
        tips["X"].Should().Be("Cut", "a declared key tip wins");
        tips["P"].Should().Be("Paste", "else the label's first free letter");
        tips["C"].Should().Be("CSV");
    }

    [AvaloniaFact]
    public void A_menu_letter_opens_it_with_letters_on_its_choices()
    {
        Ran.Clear();
        var ribbon = Show(Build(), out var window);
        var events = new List<RibbonMenuItemInvokedEventArgs>();
        ribbon.MenuItemInvoked += (_, e) => events.Add(e);
        TapAlt(window);
        Letter(window, 'h');
        Letter(window, 'c');
        Tips(ribbon).Should().BeEquivalentTo(new Dictionary<string, string> { ["S"] = "Semicolon separated", ["C"] = "Comma separated" });
        Letter(window, 's');
        Ran.Should().Equal("semi");
        events.Should().ContainSingle().Which.ParentId.Should().Be("csv");
        ribbon.IsKeyTipMode.Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_folded_group_letter_opens_its_popup_with_letters_inside()
    {
        Ran.Clear();
        var ribbon = Show(Build(RibbonGroupSize.Popup), out var window);
        TapAlt(window);
        Letter(window, 'h');
        Tips(ribbon).Values.Should().Contain(new[] { "Clipboard", "Export" }, "folded groups carry the letters");
        Letter(window, 'c');
        var chunk = ribbon.GetVisualDescendants().OfType<Button>().First(b => b.GetType().Name == "RibbonChunkButton" && b.IsEffectivelyEnabled && RibbonTests.TipText(b) == "Clipboard");
        chunk.Flyout!.IsOpen.Should().BeTrue("the letter opens the folded group");
        Tips(ribbon).Values.Should().Contain(new[] { "Paste", "Cut" });
        Letter(window, 'x');
        Ran.Should().Equal("cut");
    }

    [AvaloniaFact]
    public void Escape_steps_back_one_level_at_a_time()
    {
        var ribbon = Show(Build(), out var window);
        TapAlt(window);
        Letter(window, 'h');
        Letter(window, 'c');
        var csv = ribbon.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyEnabled && global::Avalonia.Automation.AutomationProperties.GetName(b) == "CSV");
        csv.Flyout!.IsOpen.Should().BeTrue("precondition");
        Escape(TopLevel.GetTopLevel(csv)!);
        csv.Flyout!.IsOpen.Should().BeFalse("Escape closes the menu");
        Tips(ribbon).Values.Should().Contain("Paste", "and the commands' letters are back");
        Escape(window);
        Tips(ribbon).Values.Should().Contain("Home", "then the tabs'");
        Escape(window);
        ribbon.IsKeyTipMode.Should().BeFalse("then the mode ends");
    }

    [AvaloniaFact]
    public void Alt_in_a_text_box_a_combination_or_the_key_turned_off_shows_nothing()
    {
        var box = new TextBox();
        var ribbon = Show(Build(), out var window, box);
        box.Focus();
        Dispatcher.UIThread.RunJobs();
        TapAlt(window);
        ribbon.IsKeyTipMode.Should().BeFalse("Alt in a text box is the text box's");

        window.Focus();
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        window.KeyPress(Key.F, RawInputModifiers.Alt, PhysicalKey.F, "f");
        window.KeyRelease(Key.F, RawInputModifiers.Alt, PhysicalKey.F, "f");
        window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Settle();
        ribbon.IsKeyTipMode.Should().BeFalse("Alt+F is a combination, not a tap");

        ribbon.KeyTipKey = Key.None;
        TapAlt(window);
        ribbon.IsKeyTipMode.Should().BeFalse("turned off");
    }

    [AvaloniaFact]
    public void A_letter_with_no_tip_or_a_click_ends_the_mode()
    {
        Ran.Clear();
        var ribbon = Show(Build(), out var window);
        TapAlt(window);
        Letter(window, 'q');
        ribbon.IsKeyTipMode.Should().BeFalse("a letter with no tip ends it");
        Ran.Should().BeEmpty("and runs nothing");
        TapAlt(window);
        window.MouseDown(new Point(600, 200), MouseButton.Left);
        window.MouseUp(new Point(600, 200), MouseButton.Left);
        Settle();
        ribbon.IsKeyTipMode.Should().BeFalse("a click ends it");
    }

    [AvaloniaFact]
    public void Capture_key_tip_screenshot()
    {
        var dir = System.Environment.GetEnvironmentVariable("BIRKO_SHOTS") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "birko-xaml-shots");
        System.IO.Directory.CreateDirectory(dir);
        var ribbon = Show(Build(), out var window);
        TapAlt(window);
        Letter(window, 'h');
        global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)?.Save(System.IO.Path.Combine(dir, "ribbon-keytips.png"));
    }
}
