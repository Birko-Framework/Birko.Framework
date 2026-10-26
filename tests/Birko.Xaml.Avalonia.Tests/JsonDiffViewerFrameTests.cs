using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// TASK-776 (FEATURE-020 D6): JsonDiffViewer sits in the web's data-viewer card, as <c>b-json-diff</c> does — a bordered,
/// rounded card, a header bar with a divider under it, a padded body — unless <see cref="JsonDiffViewer.IsFramed"/> is off.
/// </summary>
public class JsonDiffViewerFrameTests
{
    private const string A = """{"a":1,"b":true}""";
    private const string B = """{"a":2,"c":3}""";

    private static JsonDiffViewer Show(JsonDiffViewer v)
    {
        var window = new Window { Content = v, Width = 600, Height = 300 };
        window.Show();
        window.Measure(new Size(600, 300));
        window.Arrange(new Rect(0, 0, 600, 300));
        Dispatcher.UIThread.RunJobs();
        return v;
    }

    private static Border? Part(Control v, string cls) => v.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains(cls));

    private static IBrush? Token(Control c, string key) => c.TryFindResource(key, c.ActualThemeVariant, out var r) ? r as IBrush : null;

    [AvaloniaTheory]
    [InlineData(JsonDiffView.Tree)]
    [InlineData(JsonDiffView.SideBySide)]
    public void The_viewer_is_framed_as_a_card_by_default(JsonDiffView view)
    {
        var v = Show(new JsonDiffViewer { RawA = A, RawB = B, View = view });

        var card = Part(v, "card");
        card.Should().NotBeNull("b-json-diff renders inside .data-viewer-card");
        v.Content.Should().BeSameAs(card, "the card holds the whole viewer, header included");
        card!.BorderThickness.Should().Be(new Thickness(1));
        card.CornerRadius.Should().NotBe(default(CornerRadius));
        card.ClipToBounds.Should().BeTrue("the header's background must not paint over the rounded corners");
        card.Background.Should().BeSameAs(Token(v, "BBgTertiaryBrush"));
        card.BorderBrush.Should().BeSameAs(Token(v, "BBorderBrush"));
    }

    [AvaloniaFact]
    public void The_header_is_a_bar_with_a_divider_under_it()
    {
        var v = Show(new JsonDiffViewer { RawA = A, RawB = B });

        var bar = Part(v, "card-header");
        bar.Should().NotBeNull();
        bar!.BorderThickness.Should().Be(new Thickness(0, 0, 0, 1));
        bar.Padding.Should().Be(new Thickness(12, 4));
        bar.Background.Should().BeSameAs(Token(v, "BBgBrush"));
        bar.GetVisualDescendants().OfType<TextBlock>().Should().Contain(t => t.Classes.Contains("title"));
    }

    [AvaloniaFact]
    public void The_tree_body_is_padded()
    {
        var v = Show(new JsonDiffViewer { RawA = A, RawB = B });

        var body = v.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Classes.Contains("body"));
        body.Margin.Should().Be(new Thickness(12));
    }

    [AvaloniaFact]
    public void Side_by_side_panes_sit_in_the_padded_body_a_gap_apart()
    {
        var v = Show(new JsonDiffViewer { RawA = A, RawB = B, View = JsonDiffView.SideBySide });

        var grid = v.GetVisualDescendants().OfType<Grid>().Single(g => g.Classes.Contains("split"));
        grid.Margin.Should().Be(new Thickness(12));
        grid.ColumnDefinitions[1].Width.Should().Be(new GridLength(12), "the web's .split gap, with no frame per pane");
        grid.Children.OfType<DockPanel>().Should().HaveCount(2, "the panes sit straight in the grid, not in frames of their own");
    }

    [AvaloniaFact]
    public void Turned_off_the_viewer_is_bare()
    {
        var v = Show(new JsonDiffViewer { RawA = A, RawB = B, IsFramed = false });

        Part(v, "card").Should().BeNull();
        Part(v, "card-header").Should().BeNull();
        v.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Classes.Contains("body")).Margin.Should().Be(default(Thickness));

        v.IsFramed = true;
        Dispatcher.UIThread.RunJobs();
        Part(v, "card").Should().NotBeNull("the property re-renders the viewer");
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Capture_framed_viewer_screenshot(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "Dark" ? Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Dark : Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Light;
        try
        {
            var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "birko-xaml-shots");
            Directory.CreateDirectory(dir);
            var page = new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(16),
                Children =
                {
                    new JsonDiffViewer { RawA = A, RawB = B, LabelA = "production", LabelB = "localhost", Height = 200 },
                    new JsonDiffViewer { RawA = A, RawB = B, LabelA = "production", LabelB = "localhost", View = JsonDiffView.SideBySide, Height = 180 },
                },
            };
            var bg = new Border { Child = page };
            bg.Bind(Border.BackgroundProperty, bg.GetResourceObservable("BBgBrush"));
            var w = new Window { Content = bg, Width = 720, Height = 460 };
            w.Show();
            w.Measure(new Size(720, 460));
            w.Arrange(new Rect(0, 0, 720, 460));
            Dispatcher.UIThread.RunJobs();
            global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(w)?.Save(System.IO.Path.Combine(dir, $"json-diff-framed-{theme.ToLowerInvariant()}.png"));
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Light;
        }
    }
}
