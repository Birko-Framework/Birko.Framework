using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Theming;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>STORY-035: the markdown editor + built-in renderer (b-markdown-editor).</summary>
public class MarkdownEditorTests
{
    [AvaloniaFact]
    public void Renders_heading_list_and_code_blocks()
    {
        var root = (StackPanel)MarkdownRenderer.Render("# Title\n\npara text\n\n- one\n- two\n\n```\ncode\n```");
        root.Children.OfType<TextBlock>().Should().NotBeEmpty("heading + paragraph");
        root.Children.OfType<StackPanel>().Should().NotBeEmpty("the unordered list");
        root.Children.OfType<Border>().Should().NotBeEmpty("the fenced code block");
    }

    [AvaloniaFact]
    public void Parses_bold_and_italic_inlines()
    {
        var root = (StackPanel)MarkdownRenderer.Render("This is **bold** and *italic* text");
        var runs = root.Children.OfType<TextBlock>().First().Inlines!.OfType<Run>().ToList();
        runs.Should().Contain(r => r.Text == "bold" && r.FontWeight == FontWeight.Bold);
        runs.Should().Contain(r => r.Text == "italic" && r.FontStyle == FontStyle.Italic);
    }

    [AvaloniaFact]
    public void Editor_renders_a_live_preview()
    {
        var editor = new MarkdownEditor { Markdown = "# Hello\n\nSome **text**." };
        var window = new Window { Content = editor, Width = 600, Height = 300 };
        window.Show();
        window.Measure(new Size(600, 300));
        window.Arrange(new Rect(0, 0, 600, 300));
        Dispatcher.UIThread.RunJobs();

        // both the editor TextBox and the rendered preview exist
        editor.GetVisualDescendants().OfType<TextBox>().Should().NotBeEmpty();
        editor.GetVisualDescendants().OfType<TextBlock>().Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void Capture_markdown_editor_screenshot()
    {
        Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light;
        var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS")
                  ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
        Directory.CreateDirectory(dir);

        var editor = new MarkdownEditor
        {
            Margin = new Thickness(16),
            Markdown = "# Release notes\n\n"
                     + "Birko.Xaml **Tier-2** composites are landing.\n\n"
                     + "- tree-menu\n- command palette\n- object & xml viewers\n\n"
                     + "Use `dotnet run` to try the *gallery*.\n\n"
                     + "```\ndotnet run --project Birko.Xaml.Gallery\n```",
        };
        var page = new Border { Background = new SolidColorBrush(Color.Parse("#FFFFFF")), Child = editor };
        var window = new Window { Content = page, Width = 720, Height = 380 };
        window.Show();
        window.Measure(new Size(720, 380));
        window.Arrange(new Rect(0, 0, 720, 380));
        Dispatcher.UIThread.RunJobs();

        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        frame?.Save(Path.Combine(dir, "markdown-editor.png"));
        editor.GetVisualDescendants().OfType<TextBlock>().Should().NotBeEmpty();
    }
}
