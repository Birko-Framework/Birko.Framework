using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// Regressions for the BardStudio backport fixes:
///  - Button #2: the template now binds BorderBrush/BorderThickness (was unbound → consumer borders
///    never rendered) and the ContentPresenter follows the Button's own Background property (hover
///    now changes the property instead of hard-setting the template part).
///  - TabItem #3: PART_Border binds TemplateBinding Background (was hardcoded Transparent → a
///    consumer's :selected/:pointerover Background never painted).
/// </summary>
public class ButtonTabBackportTests
{
    private static T Show<T>(T control) where T : Control
    {
        var window = new Window { Content = control, Width = 320, Height = 200 };
        window.Show();
        window.Measure(new Size(320, 200));
        window.Arrange(new Rect(0, 0, 320, 200));
        return control;
    }

    private static ContentPresenter ButtonPresenter(Button b) =>
        b.GetVisualDescendants().OfType<ContentPresenter>().First(x => x.Name == "PART_ContentPresenter");

    [AvaloniaFact]
    public void Button_template_binds_border_from_properties()
    {
        var button = Show(new Button
        {
            Content = "Go",
            BorderBrush = Brushes.Red,
            BorderThickness = new Thickness(2)
        });

        var cp = ButtonPresenter(button);
        (cp.BorderBrush as ISolidColorBrush)!.Color.Should().Be(Colors.Red);
        cp.BorderThickness.Should().Be(new Thickness(2));
    }

    [AvaloniaFact]
    public void Button_content_presenter_follows_background_property()
    {
        // The hover/pressed styles now set the Button's Background property; because the template
        // part binds {TemplateBinding Background}, that change flows through — proven here by a
        // local Background value showing up on the part.
        var button = Show(new Button { Content = "Go", Background = Brushes.Green });

        var cp = ButtonPresenter(button);
        (cp.Background as ISolidColorBrush)!.Color.Should().Be(Colors.Green);
    }

    [AvaloniaFact]
    public void TabItem_default_background_is_transparent()
    {
        var tab = new TabItem { Header = "A", Content = "a" };
        var tabs = new TabControl { Items = { tab } };
        Show(tabs);

        var border = tab.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Border");
        (border.Background as ISolidColorBrush)!.Color.Should().Be(Colors.Transparent);
    }

    [AvaloniaFact]
    public void TabItem_border_paints_consumer_background()
    {
        var tab = new TabItem { Header = "A", Content = "a", Background = Brushes.Orange };
        var tabs = new TabControl { Items = { tab } };
        Show(tabs);

        var border = tab.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Border");
        (border.Background as ISolidColorBrush)!.Color.Should().Be(Colors.Orange);
    }
}
