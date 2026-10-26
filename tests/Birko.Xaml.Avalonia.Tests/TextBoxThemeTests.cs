using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Core.Forms;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// TASK-768: the Birko TextBox template must hand the TextBox's own properties to its presenter. It passed only Text,
/// wrapping and alignment, so a password field showed its value in plain text (reported by the FinStat desktop tester's
/// Settings "Private Key") and no placeholder ever showed (found by TASK-774).
/// </summary>
public class TextBoxThemeTests
{
    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 360, Height = 200 };
        window.Show();
        window.Measure(new Size(360, 200));
        window.Arrange(new Rect(0, 0, 360, 200));
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static TextPresenter Presenter(Control c) => c.GetVisualDescendants().OfType<TextPresenter>().Single();

    [AvaloniaFact]
    public void A_password_char_reaches_the_presenter_so_the_value_is_masked()
    {
        var box = new TextBox { PasswordChar = '●', Text = "secret-key" };
        Show(box);

        Presenter(box).PasswordChar.Should().Be('●', "the presenter is what masks; without it the key shows in plain text");
    }

    [AvaloniaFact]
    public void Reveal_password_reaches_the_presenter()
    {
        var box = new TextBox { PasswordChar = '●', Text = "secret-key", RevealPassword = true };
        Show(box);

        Presenter(box).RevealPassword.Should().BeTrue();
    }

    [AvaloniaFact]
    public void A_form_password_field_is_masked()
    {
        var model = new Settings { PrivateKey = "secret-key" };
        var form = new Form { Model = model, Fields = [new FormField { Name = nameof(Settings.PrivateKey), Label = "Private Key", Type = FieldType.Password }] };
        Show(form);

        var box = form.GetVisualDescendants().OfType<TextBox>().Single();
        Presenter(box).PasswordChar.Should().NotBe('\0');
    }

    [AvaloniaFact]
    public void The_placeholder_shows_while_the_box_is_empty_and_hides_once_it_has_text()
    {
        var box = new TextBox { Watermark = "IČO" };
        var w = Show(box);

        Watermark(box).Should().NotBeNull("the template must have a place for the placeholder");
        Watermark(box)!.Text.Should().Be("IČO");
        Watermark(box)!.IsEffectivelyVisible.Should().BeTrue();

        box.Text = "123";
        w.Measure(new Size(360, 200));
        Dispatcher.UIThread.RunJobs();
        Watermark(box)!.IsVisible.Should().BeFalse();
    }

    private static TextBlock? Watermark(TextBox box) =>
        box.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == "PART_Watermark");

    [AvaloniaFact]
    public void Caret_and_selection_brushes_and_line_spacing_reach_the_presenter()
    {
        var caret = Brushes.Red;
        var selection = Brushes.Blue;
        var box = new TextBox { Text = "abc", CaretBrush = caret, SelectionBrush = selection, LineHeight = 22, LetterSpacing = 1.5 };
        Show(box);

        var p = Presenter(box);
        p.CaretBrush.Should().BeSameAs(caret);
        p.SelectionBrush.Should().BeSameAs(selection);
        p.LineHeight.Should().Be(22);
        p.LetterSpacing.Should().Be(1.5);
    }

    [AvaloniaFact]
    public void A_selection_reaches_the_presenter()
    {
        var box = new TextBox { Text = "abcdef" };
        Show(box);

        box.SelectionStart = 1;
        box.SelectionEnd = 4;
        Dispatcher.UIThread.RunJobs();

        Presenter(box).SelectionStart.Should().Be(1);
        Presenter(box).SelectionEnd.Should().Be(4);

        box.CaretIndex = 2;
        Dispatcher.UIThread.RunJobs();
        Presenter(box).CaretIndex.Should().Be(2);
    }

    [AvaloniaFact]
    public void A_value_longer_than_the_box_scrolls_to_keep_the_caret_in_view()
    {
        // A private key in a password field: without a ScrollViewer the text ran past the box and the caret left it.
        var box = new TextBox { Width = 120, Text = new string('x', 80) };
        var w = Show(new StackPanel { Children = { box } });
        box.Focus();
        box.CaretIndex = 80;
        w.Measure(new Size(360, 200));
        w.Arrange(new Rect(0, 0, 360, 200));
        Dispatcher.UIThread.RunJobs();

        var scroller = box.GetVisualDescendants().OfType<ScrollViewer>().SingleOrDefault();
        scroller.Should().NotBeNull("the template needs the ScrollViewer Avalonia's own TextBox template has");
        scroller!.Offset.X.Should().BeGreaterThan(0, "the caret at the end is scrolled into view");
        box.Bounds.Height.Should().BeLessThan(60, "a single-line box does not grow");
    }

    [AvaloniaFact]
    public void A_multiline_box_scrolls_vertically()
    {
        var box = new TextBox { AcceptsReturn = true, Height = 80, Text = string.Join("\n", Enumerable.Range(1, 30)) };
        Show(new StackPanel { Children = { box } });

        var scroller = box.GetVisualDescendants().OfType<ScrollViewer>().Single();
        scroller.Extent.Height.Should().BeGreaterThan(scroller.Viewport.Height);
    }

    [AvaloniaFact]
    public void The_birko_theme_gives_selection_and_caret_a_brush()
    {
        var box = new TextBox { Text = "abc" };
        Show(box);

        box.SelectionBrush.Should().NotBeNull("the Birko theme replaces Fluent's, which used to supply it");
        box.CaretBrush.Should().NotBeNull();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Capture_text_box_screenshot(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "Dark" ? Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Dark : Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Light;
        try
        {
            var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "birko-xaml-shots");
            Directory.CreateDirectory(dir);
            var selected = new TextBox { Text = "selected text here" };
            var page = new StackPanel
            {
                Spacing = 8,
                Margin = new Thickness(16),
                Children =
                {
                    new TextBox { PasswordChar = '●', Text = "secret-key" },
                    new TextBox { Watermark = "IČO, comma separated" },
                    selected,
                    new TagInput { Placeholder = "Tag input placeholder" },
                },
            };
            var bg = new Border { Child = page };
            bg.Bind(Border.BackgroundProperty, bg.GetResourceObservable("BBgBrush"));
            var w = new Window { Content = bg, Width = 360, Height = 240 };
            w.Show();
            selected.SelectionStart = 0;
            selected.SelectionEnd = 8;
            w.Measure(new Size(360, 240));
            w.Arrange(new Rect(0, 0, 360, 240));
            Dispatcher.UIThread.RunJobs();
            global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(w)?.Save(System.IO.Path.Combine(dir, $"text-box-{theme.ToLowerInvariant()}.png"));
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Light;
        }
    }

    public sealed class Settings
    {
        public string? PrivateKey { get; set; }
    }
}
