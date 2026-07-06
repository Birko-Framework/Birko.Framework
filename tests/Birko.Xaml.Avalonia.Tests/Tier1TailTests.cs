using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Core.Navigation;
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
    public void Breadcrumb_renders_clickable_links_for_non_last_items_with_a_target()
    {
        var crumb = Show(new Breadcrumb
        {
            ItemsSource = new[]
            {
                new BreadcrumbItem { Label = "Home", Run = () => { } },   // has a Run → link
                new BreadcrumbItem { Label = "Users", Href = "#/users" }, // has an Href → link
                new BreadcrumbItem { Label = "Ada" },                     // current → static
            },
        });

        var links = crumb.GetVisualDescendants().OfType<Button>().ToList();
        links.Should().HaveCount(2, "the two non-last crumbs with a target are links; the last is not");
        links.Select(b => b.Content).Should().BeEquivalentTo(new object[] { "Home", "Users" });
    }

    [AvaloniaFact]
    public void Breadcrumb_click_invokes_run_and_raises_ItemInvoked()
    {
        var ran = false;
        BreadcrumbItem? invoked = null;
        var crumb = new Breadcrumb
        {
            ItemsSource = new[]
            {
                new BreadcrumbItem { Label = "Home", Href = "#/", Run = () => ran = true },
                new BreadcrumbItem { Label = "Here" },
            },
        };
        crumb.ItemInvoked += (_, item) => invoked = item;
        Show(crumb);

        var home = crumb.GetVisualDescendants().OfType<Button>().First(b => (string?)b.Content == "Home");
        home.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        ran.Should().BeTrue("the crumb's Run action fires on click");
        invoked.Should().NotBeNull();
        invoked!.Href.Should().Be("#/", "ItemInvoked carries the clicked item so a shell can route on its Href");
    }

    [AvaloniaFact]
    public void Breadcrumb_last_item_is_never_a_link_even_with_a_run()
    {
        var crumb = Show(new Breadcrumb
        {
            ItemsSource = new[]
            {
                new BreadcrumbItem { Label = "Home", Run = () => { } },
                new BreadcrumbItem { Label = "Current", Run = () => { } }, // last → current, static
            },
        });

        var links = crumb.GetVisualDescendants().OfType<Button>().ToList();
        links.Should().ContainSingle().Which.Content.Should().Be("Home");
        crumb.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should()
            .Contain("Current", "the last crumb is the current location, rendered as static text");
    }

    [AvaloniaFact]
    public void ListBoxItem_selected_uses_token_foreground()
    {
        var list = Show(new ListBox { ItemsSource = new[] { "Alpha", "Beta", "Gamma" } });
        list.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        var item = list.GetVisualDescendants().OfType<ListBoxItem>().First(i => i.IsSelected);
        (item.Foreground as ISolidColorBrush)!.Color.Should().Be(Color.Parse("#2563EB"), "selected list items use the primary token");
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
