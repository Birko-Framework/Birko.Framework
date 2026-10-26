using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>TASK-774 (FEATURE-020): TagInput, the port of b-tag-input — separators, duplicates, max count, the reject reasons, keys and paste.</summary>
public class TagInputTests
{
    private static TagInput Show(TagInput t)
    {
        var window = new Window { Content = t, Width = 480, Height = 200 };
        window.Show();
        Pump(t);
        return t;
    }

    private static void Pump(Control c)
    {
        var root = (Window)c.GetVisualRoot()!;
        root.Measure(new Size(480, 200));
        root.Arrange(new Rect(0, 0, 480, 200));
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBox Box(TagInput t) => t.GetVisualDescendants().OfType<TextBox>().Single();

    private static void Type(TagInput t, string text)
    {
        var box = Box(t);
        box.Text = (box.Text ?? "") + text;
        Pump(t);
    }

    private static void Key(TagInput t, Key key)
    {
        Box(t).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        Pump(t);
    }

    private static List<string?> Chips(TagInput t) =>
        t.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("chip"))
            .Select(b => b.GetVisualDescendants().OfType<TextBlock>().First().Text).ToList();

    [AvaloniaFact]
    public void Typing_a_separator_commits_the_tag_before_it_and_keeps_the_rest()
    {
        var t = Show(new TagInput());
        var changes = 0;
        t.TagsChanged += (_, _) => changes++;

        Type(t, "12345678,876");

        t.Tags.Should().Equal("12345678");
        t.PendingText.Should().Be("876");
        Chips(t).Should().Equal("12345678");
        changes.Should().Be(1);

        Type(t, " , second  ,");
        t.Tags.Should().Equal("12345678", "876", "second");
    }

    [AvaloniaFact]
    public void Typing_keeps_the_focus_in_the_box_as_chips_are_added()
    {
        var t = Show(new TagInput());
        Box(t).Focus();
        Type(t, "a,");
        Type(t, "b,");

        t.Tags.Should().Equal("a", "b");
        Box(t).IsFocused.Should().BeTrue("the web lost the second value when a re-render replaced the focused input");
    }

    [AvaloniaFact]
    public void Enter_and_tab_commit_the_pending_text()
    {
        var t = Show(new TagInput());
        Type(t, " a ");
        Key(t, global::Avalonia.Input.Key.Enter);
        Type(t, "b");
        Key(t, global::Avalonia.Input.Key.Tab);

        t.Tags.Should().Equal("a", "b");
        t.PendingText.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void A_duplicate_is_refused_with_its_reason_unless_duplicates_are_allowed()
    {
        var t = Show(new TagInput());
        var rejected = new List<(string, TagRejectReason)>();
        t.TagRejected += (_, e) => rejected.Add((e.Tag, e.Reason));

        Type(t, "x,x,");

        t.Tags.Should().Equal("x");
        rejected.Should().Equal(("x", TagRejectReason.Duplicate));

        t.AllowDuplicates = true;
        Type(t, "x,");
        t.Tags.Should().Equal("x", "x");
    }

    [AvaloniaFact]
    public void Past_the_max_count_a_tag_is_refused_with_its_reason()
    {
        var t = Show(new TagInput { MaxCount = 2 });
        var rejected = new List<(string, TagRejectReason)>();
        t.TagRejected += (_, e) => rejected.Add((e.Tag, e.Reason));

        Type(t, "a,b,c,");

        t.Tags.Should().Equal("a", "b");
        rejected.Should().Equal(("c", TagRejectReason.MaxCount));
    }

    [AvaloniaFact]
    public void Custom_separators_replace_the_defaults()
    {
        var t = Show(new TagInput { Separators = ";|-" });
        Type(t, "a;b|c-d,e");

        t.Tags.Should().Equal("a", "b", "c");
        t.PendingText.Should().Be("d,e", "a comma is not a separator once Separators is set");
    }

    [AvaloniaFact]
    public void Pasting_text_with_separators_adds_every_part_and_without_inserts_it()
    {
        var t = Show(new TagInput { Separators = ",;" });

        t.PasteText("a, b;c");
        t.Tags.Should().Equal("a", "b", "c");
        t.PendingText.Should().BeEmpty("unlike typing, a paste commits its last part too, as on the web");

        t.PasteText("plain");
        t.Tags.Should().HaveCount(3);
        t.PendingText.Should().Be("plain");
    }

    [AvaloniaFact]
    public void Backspace_in_the_empty_box_removes_the_last_tag_and_a_chip_button_removes_its_own()
    {
        var t = Show(new TagInput());
        t.SetTags(["p", "q", "r"]);
        Pump(t);
        var removed = new List<string>();
        t.TagRemoved += (_, e) => removed.Add(e.Tag);

        Key(t, global::Avalonia.Input.Key.Back);
        t.Tags.Should().Equal("p", "q");

        var removeP = t.GetVisualDescendants().OfType<Button>()
            .Single(b => global::Avalonia.Automation.AutomationProperties.GetName(b) == "Remove p");
        removeP.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump(t);

        t.Tags.Should().Equal("q");
        removed.Should().Equal("r", "p");
    }

    [AvaloniaFact]
    public void Leaving_the_field_commits_the_pending_text()
    {
        var t = new TagInput();
        var other = new TextBox();
        var window = new Window { Content = new StackPanel { Children = { t, other } }, Width = 480, Height = 200 };
        window.Show();
        Pump(t);
        Box(t).Focus();
        Type(t, "late");

        other.Focus();
        Pump(t);

        t.Tags.Should().Equal("late");
    }

    [AvaloniaFact]
    public void SetTags_and_a_bound_list_go_through_the_rules_without_events()
    {
        var t = Show(new TagInput { MaxCount = 2 });
        var events = 0;
        t.TagsChanged += (_, _) => events++;
        t.TagRejected += (_, _) => events++;

        t.SetTags(["a", "a", "b", "c"]);
        t.Tags.Should().Equal("a", "b");

        t.Tags = ["x", "y"];
        Chips(t).Should().Equal("x", "y");
        events.Should().Be(0);
    }

    [AvaloniaFact]
    public void Tags_bind_two_way()
    {
        var vm = new Vm { Tags = ["seed"] };
        var t = Show(new TagInput { DataContext = vm });
        t.Bind(TagInput.TagsProperty, new Binding(nameof(Vm.Tags)));
        Pump(t);
        Chips(t).Should().Equal("seed");

        Type(t, "more,");

        vm.Tags.Should().Equal("seed", "more");
    }

    [AvaloniaFact]
    public void Value_joins_with_commas_and_splits_on_them()
    {
        var t = Show(new TagInput());
        t.Value = "a, b,,c";
        t.Tags.Should().Equal("a", "b", "c");
        t.Value.Should().Be("a,b,c");
    }

    [AvaloniaFact]
    public void Clear_empties_and_raises_changed()
    {
        var t = Show(new TagInput());
        t.SetTags(["a"]);
        var changes = 0;
        t.TagsChanged += (_, _) => changes++;

        t.Clear();

        t.Tags.Should().BeEmpty();
        changes.Should().Be(1);
    }

    [AvaloniaFact]
    public void Read_only_shows_chips_without_the_box_or_remove_buttons()
    {
        var t = Show(new TagInput { IsReadOnly = true });
        t.SetTags(["a", "b"]);
        Pump(t);

        Chips(t).Should().Equal("a", "b");
        Box(t).IsVisible.Should().BeFalse();
        t.GetVisualDescendants().OfType<Button>().Should().BeEmpty();
    }

    [AvaloniaFact]
    public void The_placeholder_shows_only_while_there_are_no_tags()
    {
        var t = Show(new TagInput { Placeholder = "IČO" });
        Box(t).Watermark.Should().Be("IČO");

        Type(t, "1,");
        Box(t).Watermark.Should().BeNull();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Capture_tag_input_screenshot(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "Dark" ? Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Dark : Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Light;
        try
        {
            var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS") ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
            Directory.CreateDirectory(dir);
            var filled = new TagInput();
            filled.SetTags(["35757442", "31411801", "00177041", "36631124", "46990119"]);
            var empty = new TagInput { Placeholder = "IČO, comma separated" };
            var error = new TagInput { HasError = true };
            error.SetTags(["1234"]);
            var readOnly = new TagInput { IsReadOnly = true };
            readOnly.SetTags(["sk", "cz"]);
            var page = new StackPanel { Spacing = 10, Margin = new Thickness(16), Children = { filled, empty, error, readOnly } };
            var bg = new Border { Child = page };
            bg.Bind(Border.BackgroundProperty, bg.GetResourceObservable("BBgBrush"));
            var window = new Window { Content = bg, Width = 420, Height = 260 };
            window.Show();
            window.Measure(new Size(420, 260));
            window.Arrange(new Rect(0, 0, 420, 260));
            Dispatcher.UIThread.RunJobs();
            global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)?.Save(Path.Combine(dir, $"tag-input-{theme.ToLowerInvariant()}.png"));
            filled.Tags.Should().HaveCount(5);
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Light;
        }
    }

    public sealed class Vm : System.ComponentModel.INotifyPropertyChanged
    {
        private IReadOnlyList<string> _tags = [];

        public IReadOnlyList<string> Tags
        {
            get => _tags;
            set { _tags = value; PropertyChanged?.Invoke(this, new(nameof(Tags))); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
