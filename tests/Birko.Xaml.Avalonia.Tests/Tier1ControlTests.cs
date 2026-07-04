using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Theming;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>STORY-034 Tier-1 controls: each is token-driven and re-themes per variant.</summary>
public class Tier1ControlTests
{
    private static Application App => Application.Current!;

    private static Color Bg(TemplatedControl c) =>
        (c.Background as ISolidColorBrush)?.Color
        ?? throw new Xunit.Sdk.XunitException("Background is not a solid colour brush");

    private static T Show<T>(T control) where T : Control
    {
        var window = new Window { Content = control, Width = 320, Height = 200 };
        window.Show();
        window.Measure(new Size(320, 200));
        window.Arrange(new Rect(0, 0, 320, 200));
        return control;
    }

    [AvaloniaFact]
    public void ComboBox_surface_reskins_per_theme()
    {
        var combo = Show(new ComboBox());
        App.RequestedThemeVariant = BirkoThemeVariants.Light;
        Bg(combo).Should().Be(Color.Parse("#FFFFFF"));
        App.RequestedThemeVariant = BirkoThemeVariants.Dark;
        Bg(combo).Should().Be(Color.Parse("#0F172A"));
    }

    [AvaloniaFact]
    public void ProgressBar_fill_uses_primary_per_theme()
    {
        var bar = Show(new ProgressBar { Value = 50 });
        App.RequestedThemeVariant = BirkoThemeVariants.Light;
        (bar.Foreground as ISolidColorBrush)!.Color.Should().Be(Color.Parse("#2563EB"));
        App.RequestedThemeVariant = BirkoThemeVariants.Neon;
        (bar.Foreground as ISolidColorBrush)!.Color.Should().Be(Color.Parse("#8CFFB0"));
    }

    [AvaloniaFact]
    public void CheckBox_checked_box_uses_primary()
    {
        var cb = Show(new CheckBox { IsChecked = true });
        App.RequestedThemeVariant = BirkoThemeVariants.Light;
        var box = cb.GetVisualDescendants().OfType<Border>().First(b => b.Name == "box");
        (box.Background as ISolidColorBrush)!.Color.Should().Be(Color.Parse("#2563EB"));
    }

    [AvaloniaFact]
    public void RadioButton_checked_dot_is_visible()
    {
        var rb = Show(new RadioButton { IsChecked = true });
        var dot = rb.GetVisualDescendants().OfType<Ellipse>().First(e => e.Name == "dot");
        dot.IsVisible.Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData("BTag")]
    [InlineData("BBadge")]
    [InlineData("BCard")]
    public void Named_content_themes_are_findable(string key)
    {
        App.TryFindResource(key, out var theme).Should().BeTrue();
        theme.Should().BeOfType<ControlTheme>();
    }

    [AvaloniaFact]
    public void TabItem_selected_uses_primary_foreground()
    {
        var tabs = new TabControl
        {
            Items = { new TabItem { Header = "A", Content = "a" }, new TabItem { Header = "B", Content = "b" } },
        };
        Show(tabs);
        App.RequestedThemeVariant = BirkoThemeVariants.Light;
        var selected = (TabItem)tabs.Items[0]!;
        selected.IsSelected.Should().BeTrue();
        (selected.Foreground as ISolidColorBrush)!.Color.Should().Be(Color.Parse("#2563EB"));
    }
}
