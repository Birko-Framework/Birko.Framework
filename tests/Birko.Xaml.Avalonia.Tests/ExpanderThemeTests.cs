using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>TASK-775 (FEATURE-020 D10): the Birko ControlTheme for Avalonia's Expander, the look of b-accordion.</summary>
public class ExpanderThemeTests
{
    private static Window Show(Control content, double width = 360, double height = 300)
    {
        var window = new Window { Content = content, Width = width, Height = height };
        window.Show();
        Pump(window, width, height);
        return window;
    }

    private static void Pump(Window w, double width = 360, double height = 300)
    {
        w.Measure(new Size(width, height));
        w.Arrange(new Rect(0, 0, width, height));
        Dispatcher.UIThread.RunJobs();
    }

    private static ToggleButton Header(Expander e) =>
        e.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Name == "ExpanderHeader");

    private static Control Panel(Expander e) => e.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Panel");

    [AvaloniaFact]
    public void The_birko_theme_is_the_expanders_theme()
    {
        Application.Current!.TryGetResource(typeof(Expander), null, out var theme).Should().BeTrue();
        var e = new Expander { Header = "Section", Content = new TextBlock { Text = "Body" } };
        var w = Show(e);

        e.GetVisualDescendants().OfType<Path>().Should().Contain(p => p.Name == "PART_Chevron", "the Birko template, not Fluent's");
        Header(e).Theme.Should().BeSameAs(Application.Current.FindResource("BExpanderHeader"));
        w.Close();
    }

    [AvaloniaFact]
    public void Toggling_shows_and_hides_the_content_and_turns_the_chevron()
    {
        var body = new TextBlock { Text = "Body" };
        var e = new Expander { Header = "Section", Content = body };
        var w = Show(e);

        Panel(e).IsVisible.Should().BeFalse();

        // As a user does it: a key on the header, which toggles through the template binding (a local IsChecked would
        // replace that binding, and code closing the section afterwards would no longer reach the header).
        Header(e).Focus();
        Header(e).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
        Header(e).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Space });
        Pump(w);
        e.IsExpanded.Should().BeTrue();
        Panel(e).IsVisible.Should().BeTrue();
        body.IsEffectivelyVisible.Should().BeTrue();
        var chevron = e.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_Chevron");
        chevron.RenderTransform.Should().NotBeNull("the chevron turns 90° when open");

        e.IsExpanded = false;
        Pump(w);
        Header(e).IsChecked.Should().BeFalse();
        Panel(e).IsVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Space_on_the_focused_header_toggles_it()
    {
        var e = new Expander { Header = "Section", Content = "Body" };
        var w = Show(e);
        var header = Header(e);
        header.Focus();

        header.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
        header.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Space });
        Pump(w);

        e.IsExpanded.Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData(ExpandDirection.Down, Dock.Top)]
    [InlineData(ExpandDirection.Up, Dock.Bottom)]
    [InlineData(ExpandDirection.Left, Dock.Right)]
    [InlineData(ExpandDirection.Right, Dock.Left)]
    public void Every_expand_direction_puts_the_header_on_its_side(ExpandDirection direction, Dock dock)
    {
        var e = new Expander { Header = "Section", Content = "Body", ExpandDirection = direction, IsExpanded = true };
        Show(e);

        DockPanel.GetDock(Header(e)).Should().Be(dock);
        Panel(e).IsVisible.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Colours_come_from_the_theme_tokens()
    {
        var e = new Expander { Header = "Section", Content = "Body" };
        Show(e);

        e.BorderBrush.Should().BeSameAs(Application.Current!.FindResource(ThemeVariant.Light, "BBorderBrush"));
        e.Background.Should().BeSameAs(Application.Current!.FindResource(ThemeVariant.Light, "BBgBrush"));
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Capture_expander_screenshot(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "Dark" ? Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Dark : Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Light;
        try
        {
            var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "birko-xaml-shots");
            Directory.CreateDirectory(dir);
            var page = new StackPanel
            {
                Spacing = 8,
                Margin = new Thickness(16),
                Children =
                {
                    new Expander { Header = "Production — 31 differences", IsExpanded = true, Content = new TextBlock { Text = "Items[2].Revenue: 1000 → 1007", TextWrapping = TextWrapping.Wrap } },
                    new Expander { Header = "Test — equal", Content = "Body" },
                    new Expander { Header = "Disabled section", IsEnabled = false, Content = "Body" },
                },
            };
            var bg = new Border { Child = page };
            bg.Bind(Border.BackgroundProperty, bg.GetResourceObservable("BBgSecondaryBrush"));
            var w = Show(bg, 420, 260);
            global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(w)?.Save(System.IO.Path.Combine(dir, $"expander-{theme.ToLowerInvariant()}.png"));
            w.Close();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Light;
        }
    }
}
