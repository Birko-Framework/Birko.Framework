using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Theming;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>STORY-034 tail: ToggleSwitch, Spinner, Breadcrumb, dropdown-menu themes.</summary>
public class Tier1TailTests
{
    private static Application App => Application.Current!;

    private static T Show<T>(T control) where T : Control
    {
        App.RequestedThemeVariant = BirkoThemeVariants.Light; // normalize shared app state
        var window = new Window { Content = control, Width = 320, Height = 200 };
        window.Show();
        window.Measure(new Size(320, 200));
        window.Arrange(new Rect(0, 0, 320, 200));
        return control;
    }

    [AvaloniaFact]
    public void ToggleSwitch_checked_track_uses_primary()
    {
        var toggle = Show(new ToggleSwitch { IsChecked = true });
        var track = toggle.GetVisualDescendants().OfType<Border>().First(b => b.Name == "track");
        (track.Background as ISolidColorBrush)!.Color.Should().Be(Color.Parse("#2563EB"));
    }

    [AvaloniaFact]
    public void BusySpinner_theme_applies_default_size_and_arc()
    {
        var spinner = Show(new BusySpinner());
        spinner.Width.Should().Be(24, "BSpinnerSize token (1.5rem) drives the default size — proves the theme applied");
        spinner.GetVisualDescendants().OfType<Arc>().Should().NotBeEmpty("the template renders a rotating arc");
    }

    [AvaloniaFact]
    public void Breadcrumb_renders_crumbs_with_separators()
    {
        var crumb = Show(new Breadcrumb { ItemsSource = new[] { "Home", "Users", "Ada" } });
        var texts = crumb.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        texts.Should().Contain("Home");
        texts.Should().Contain("Ada");
        texts.Count(t => t == "/").Should().Be(2, "two separators between three crumbs");
    }

    [AvaloniaFact]
    public void Dropdown_menu_themes_are_registered()
    {
        App.TryGetResource(typeof(MenuItem), null, out var item).Should().BeTrue();
        item.Should().BeOfType<global::Avalonia.Styling.ControlTheme>();
        App.TryGetResource(typeof(MenuFlyoutPresenter), null, out var presenter).Should().BeTrue();
        presenter.Should().BeOfType<global::Avalonia.Styling.ControlTheme>();
    }
}
