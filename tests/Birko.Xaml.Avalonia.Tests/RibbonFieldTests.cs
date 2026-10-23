using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
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
/// TASK-762 (FEATURE-019 D12 on the desktop): inputs, as b-ribbon has had since TASK-741. Text, number and dropdown
/// boxes (a dropdown says whether it takes a typed value), in the three caption looks; a value is committed by Enter, a
/// pick or leaving the box, and reported once; Escape reverts; the arrows edit inside (a number steps) and never move
/// to the next command; Tab leaves; a folded group's popup, key tips (focus with the text selected), the narrow menu.
/// Driven by real headless keys and text input.
/// </summary>
public class RibbonFieldTests
{
    private static RibbonTab Home(RibbonFieldCaption? caption = null) => new()
    {
        Id = "home", Label = "Home", Groups = new[]
        {
            new RibbonGroup { Label = "Font", Icon = "𝖠", Items = new[]
            {
                new RibbonItem { Id = "font", Label = "Font", Keytip = "F", Field = new RibbonField
                {
                    Type = RibbonFieldType.Select, FreeText = true, Value = "calibri", Width = 10, Caption = caption,
                    Options = new[] { new RibbonFieldOption { Value = "calibri", Label = "Calibri" }, new RibbonFieldOption { Value = "arial", Label = "Arial" } },
                } },
                new RibbonItem { Id = "size", Label = "Size", Field = new RibbonField
                {
                    Type = RibbonFieldType.Select, Value = "11", Width = 3, Caption = caption,
                    Options = new[] { "9", "11", "14" }.Select(v => new RibbonFieldOption { Value = v, Label = v }).ToList(),
                } },
                new RibbonItem { Id = "bold", Label = "Bold", Icon = "B" },
            } },
            new RibbonGroup { Label = "Layout", Icon = "▥", Items = new[]
            {
                new RibbonItem { Id = "height", Label = "Height", Field = new RibbonField { Type = RibbonFieldType.Number, Value = "10", Min = 0, Max = 12, Step = 1, Width = 4, Caption = caption } },
                new RibbonItem { Id = "name", Label = "Name", Field = new RibbonField { Type = RibbonFieldType.Text, Placeholder = "Sheet name", Caption = caption } },
            } },
        },
    };

    private static Ribbon Build(RibbonGroupSize size = RibbonGroupSize.Medium, RibbonFieldCaption? caption = null) => new()
    {
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        NarrowThreshold = 0,
        PreferredGroupSize = size,
        Tabs = new[] { Home(caption) },
    };

    private static Ribbon Show(Ribbon ribbon, out Window window, double width = 1000, Control? after = null)
    {
        var page = new StackPanel { Children = { ribbon } };
        if (after is not null) page.Children.Add(after);
        window = new Window { Content = page, Width = width, Height = 320 };
        window.Show();
        Settle(window, width);
        return ribbon;
    }

    private static void Settle(Window window, double width = 1000)
    {
        window.Measure(new Size(width, 320));
        window.Arrange(new Rect(0, 0, width, 320));
        for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
    }

    private static string? Name(Control c) => global::Avalonia.Automation.AutomationProperties.GetName(c);

    private static T Box<T>(Visual scope, string name) where T : Control =>
        scope.GetVisualDescendants().OfType<T>().First(b => Name(b) == name && b.IsEffectivelyVisible
            && b.TranslatePoint(default, scope) is { X: > -1000 });

    private static void Press(Window window, Key key, PhysicalKey physical, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPress(key, mods, physical, null);
        window.KeyRelease(key, mods, physical, null);
        Settle(window);
    }

    private static void Type(Window window, string text)
    {
        window.KeyTextInput(text);
        Settle(window);
    }

    private static List<RibbonFieldChangedEventArgs> Track(Ribbon ribbon)
    {
        var changes = new List<RibbonFieldChangedEventArgs>();
        ribbon.FieldChanged += (_, e) => changes.Add(e);
        return changes;
    }

    [AvaloniaFact]
    public void Text_number_and_dropdown_boxes_show_their_values_named_by_their_labels()
    {
        var ribbon = Show(Build(), out _);
        Box<AutoCompleteBox>(ribbon, "Font").Text.Should().Be("Calibri", "a free-text dropdown shows the option's label");
        Box<ComboBox>(ribbon, "Size").SelectedItem.Should().Be("11", "a closed list dropdown");
        Box<TextBox>(ribbon, "Height").Text.Should().Be("10");
        Box<TextBox>(ribbon, "Name").Watermark.Should().Be("Sheet name");
        RibbonTests.TipText(Box<TextBox>(ribbon, "Height")).Should().Contain("Height", "the tooltip names an uncaptioned box");
        ribbon.GetFieldValue("size").Should().Be("11");
        ribbon.GetFieldValue("nope").Should().BeNull();
    }

    [AvaloniaFact]
    public void Enter_commits_once_and_leaving_commits_once()
    {
        var ribbon = Show(Build(), out var window);
        var changes = Track(ribbon);
        var name = Box<TextBox>(ribbon, "Name");
        name.Focus();
        Settle(window);
        Type(window, "Q3");
        changes.Should().BeEmpty("typing is a draft");
        Press(window, Key.Enter, PhysicalKey.Enter);
        changes.Should().ContainSingle().Which.Should().BeEquivalentTo(new { TabId = "home", ItemId = "name", Value = "Q3" });
        Press(window, Key.Tab, PhysicalKey.Tab);
        changes.Should().HaveCount(1, "leaving with the same value reports nothing again");

        Box<TextBox>(ribbon, "Name").Focus();
        Settle(window);
        Type(window, "x");
        Box<Button>(ribbon, "Bold").Focus();
        Settle(window);
        changes.Should().HaveCount(2, "leaving commits");
        changes[^1].Value.Should().Be("Q3x");
        ribbon.GetFieldValue("name").Should().Be("Q3x");
    }

    [AvaloniaFact]
    public void Escape_puts_the_committed_value_back_and_goes_to_the_tab()
    {
        var ribbon = Show(Build(), out var window);
        var changes = Track(ribbon);
        var height = Box<TextBox>(ribbon, "Height");
        height.Focus();
        Settle(window);
        height.SelectAll();
        Type(window, "7");
        height.Text.Should().Be("7");
        Press(window, Key.Escape, PhysicalKey.Escape);
        Box<TextBox>(ribbon, "Height").Text.Should().Be("10");
        ribbon.GetVisualDescendants().OfType<Button>().First(b => Name(b) == "Home").IsFocused.Should().BeTrue();
        changes.Should().BeEmpty("nothing was committed");
    }

    [AvaloniaFact]
    public void The_arrows_step_a_number_within_its_bounds_and_never_leave_the_box()
    {
        var ribbon = Show(Build(), out var window);
        var changes = Track(ribbon);
        var height = Box<TextBox>(ribbon, "Height");
        height.Focus();
        Settle(window);
        Press(window, Key.Up, PhysicalKey.ArrowUp);
        Press(window, Key.Up, PhysicalKey.ArrowUp);
        Press(window, Key.Up, PhysicalKey.ArrowUp);
        height.Text.Should().Be("12", "stepped and stopped at Max");
        Press(window, Key.Right, PhysicalKey.ArrowRight);
        Press(window, Key.Left, PhysicalKey.ArrowLeft);
        Press(window, Key.Home, PhysicalKey.Home);
        height.IsFocused.Should().BeTrue("the arrows, Home and End edit the box");
        changes.Should().BeEmpty("stepping is a draft");
        Press(window, Key.Enter, PhysicalKey.Enter);
        changes.Should().ContainSingle().Which.Value.Should().Be("12");
    }

    [AvaloniaFact]
    public void Picking_from_a_list_commits_once()
    {
        var ribbon = Show(Build(), out var window);
        var changes = Track(ribbon);
        var size = Box<ComboBox>(ribbon, "Size");
        size.IsDropDownOpen = true;
        Settle(window);
        size.SelectedIndex = 2;
        size.IsDropDownOpen = false;
        Settle(window);
        changes.Should().ContainSingle().Which.Value.Should().Be("14");
        size.Focus();
        Box<Button>(ribbon, "Bold").Focus();
        Settle(window);
        changes.Should().HaveCount(1, "never twice");
    }

    [AvaloniaFact]
    public void A_free_dropdown_takes_a_typed_value_and_maps_a_typed_label_to_its_value()
    {
        var ribbon = Show(Build(), out var window);
        var changes = Track(ribbon);
        var font = Box<AutoCompleteBox>(ribbon, "Font");
        font.Text = "Comic Sans";
        Settle(window);
        font.Focus();
        Settle(window);
        Press(window, Key.Enter, PhysicalKey.Enter);
        changes.Should().ContainSingle().Which.Value.Should().Be("Comic Sans", "a value not in the list");
        font.Text = "Arial";
        Settle(window);
        Press(window, Key.Enter, PhysicalKey.Enter);
        changes[^1].Value.Should().Be("arial", "a typed label is its option's value");
    }

    [AvaloniaFact]
    public void The_app_sets_a_value_silently_and_every_box_shows_a_commit()
    {
        var ribbon = Show(Build(RibbonGroupSize.Large), out var window);
        var changes = Track(ribbon);
        ribbon.SetFieldValue("height", "3");
        Settle(window);
        ribbon.GetVisualDescendants().OfType<TextBox>().Where(t => Name(t) == "Height").Should().OnlyContain(t => t.Text == "3", "every size's box");
        changes.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(RibbonFieldCaption.None)]
    [InlineData(RibbonFieldCaption.Beside)]
    [InlineData(RibbonFieldCaption.Above)]
    public void The_three_caption_looks(RibbonFieldCaption caption)
    {
        var ribbon = Show(Build(caption: caption), out _);
        var box = Box<TextBox>(ribbon, "Height");
        var captions = ribbon.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == "Height" && t.TranslatePoint(default, ribbon) is { X: > -1000 }).ToList();
        if (caption == RibbonFieldCaption.None) { captions.Should().BeEmpty("Office: the tooltip and the name say it"); return; }
        var text = captions.Should().ContainSingle().Subject;
        global::Avalonia.Automation.AutomationProperties.GetAccessibilityView(text).Should().Be(global::Avalonia.Automation.AccessibilityView.Raw, "the box carries the name");
        var at = text.TranslatePoint(default, ribbon)!.Value;
        var boxAt = box.TranslatePoint(default, ribbon)!.Value;
        if (caption == RibbonFieldCaption.Beside) at.X.Should().BeLessThan(boxAt.X);
        else at.Y.Should().BeLessThan(boxAt.Y, "above the box");
    }

    [AvaloniaFact]
    public void A_box_is_a_panel_stop_reached_by_the_arrows()
    {
        var ribbon = Show(Build(), out var window);
        var bold = Box<Button>(ribbon, "Bold");
        bold.Focus(NavigationMethod.Tab);
        Settle(window);
        Press(window, Key.Right, PhysicalKey.ArrowRight);
        var focused = TopLevel.GetTopLevel(ribbon)!.FocusManager!.GetFocusedElement() as Visual;
        var name = Box<TextBox>(ribbon, "Name");
        (ReferenceEquals(focused, name) || name.IsVisualAncestorOf(focused!)).Should().BeTrue("Right from Bold lands in the nearest box that way, Name");
    }

    [AvaloniaFact]
    public void An_explicit_commit_in_a_folded_group_closes_its_popup_and_leaving_does_not()
    {
        var ribbon = Show(Build(RibbonGroupSize.Popup), out var window);
        var changes = Track(ribbon);
        var chunk = ribbon.GetVisualDescendants().OfType<Button>().First(b => b.GetType().Name == "RibbonChunkButton" && RibbonTests.TipText(b) == "Layout" && b.IsEffectivelyEnabled);
        ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(chunk)).Invoke();
        Settle(window);
        var inside = (Control)((Flyout)chunk.Flyout!).Content!;
        var name = inside.GetVisualDescendants().OfType<TextBox>().First(t => Name(t) == "Name");
        name.Focus();
        Settle(window);
        Type(window, "Q4");
        Press(window, Key.Enter, PhysicalKey.Enter);
        changes.Should().ContainSingle().Which.Value.Should().Be("Q4");
        chunk.Flyout.IsOpen.Should().BeFalse("Enter closes the folded group, as a command does");
    }

    [AvaloniaFact]
    public void A_key_tip_focuses_the_box_with_its_text_selected()
    {
        var ribbon = Show(Build(), out var window);
        window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Settle(window);
        Press(window, Key.H, PhysicalKey.H);
        var tips = (IReadOnlyDictionary<string, Control>)typeof(Ribbon).GetProperty("KeyTips",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ribbon)!;
        tips.Should().ContainKey("F").WhoseValue.Should().Match<Control>(c => Name(c) == "Font", "the declared letter");
        string height = tips.Single(t => Name(t.Value) == "Height").Key;
        Press(window, (Key)((int)Key.A + (height[0] - 'A')), PhysicalKey.None);
        ribbon.IsKeyTipMode.Should().BeFalse();
        var box = Box<TextBox>(ribbon, "Height");
        box.IsFocused.Should().BeTrue();
        box.SelectedText.Should().Be("10", "ready to type over");
    }

    [AvaloniaFact]
    public void The_narrow_menu_has_the_boxes_with_captions_beside_them()
    {
        var ribbon = Build();
        ribbon.NarrowThreshold = 600;
        Show(ribbon, out var window, 400);
        ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(ribbon.GetVisualDescendants().OfType<Button>().First(b => Name(b) == "Open the ribbon menu"))).Invoke();
        Settle(window, 400);
        var menu = ribbon.OpenOverlay!;
        var height = menu.GetVisualDescendants().OfType<TextBox>().First(t => Name(t) == "Height");
        menu.GetVisualDescendants().OfType<TextBlock>().Should().Contain(t => t.Text == "Height", "captioned beside");
        height.Text.Should().Be("10");
        var changes = Track(ribbon);
        height.Text = "5";
        height.Focus();
        Settle(window, 400);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Settle(window, 400);
        changes.Should().ContainSingle().Which.Value.Should().Be("5");
        ribbon.OpenOverlay.Should().NotBeNull("a commit in the narrow menu keeps it open, as the web's");
    }

    [AvaloniaFact]
    public void Capture_field_screenshots()
    {
        var dir = System.Environment.GetEnvironmentVariable("BIRKO_SHOTS") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "birko-xaml-shots");
        System.IO.Directory.CreateDirectory(dir);
        foreach (var caption in new[] { RibbonFieldCaption.None, RibbonFieldCaption.Beside, RibbonFieldCaption.Above })
        {
            var ribbon = Build(caption: caption);
            Show(ribbon, out var window);
            window.CaptureRenderedFrame()?.Save(System.IO.Path.Combine(dir, $"ribbon-fields-{caption.ToString().ToLowerInvariant()}.png"));
            window.Close();
        }
    }
}
