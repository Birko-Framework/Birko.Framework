using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Birko.Xaml.Avalonia.Theming;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// STORY-031 gate: the first restyled controls (Button/TextBox + Card/Badge themes) consume design
/// tokens and re-theme live. Proves the token → ControlTheme → live-swap pipeline end to end.
/// </summary>
public class ControlThemeTests
{
    private static Application App => Application.Current!;

    private static Color BackgroundColor(TemplatedControl c) =>
        (c.Background as ISolidColorBrush)?.Color
        ?? throw new Xunit.Sdk.XunitException("Background is not a solid colour brush");

    [AvaloniaFact]
    public void Button_uses_primary_token_and_reskins_per_theme()
    {
        var button = new Button { Content = "Save" };
        var window = new Window { Content = button };
        window.Show();

        App.RequestedThemeVariant = BirkoThemeVariants.Light;
        BackgroundColor(button).Should().Be(Color.Parse("#2563EB"));

        App.RequestedThemeVariant = BirkoThemeVariants.Neon;
        BackgroundColor(button).Should().Be(Color.Parse("#8CFFB0"));

        App.RequestedThemeVariant = BirkoThemeVariants.Finstat;
        BackgroundColor(button).Should().Be(Color.Parse("#25BA7A"));
    }

    [AvaloniaFact]
    public void Button_corner_radius_binds_from_double_token()
    {
        // Confirms the double token (BRadius) flows into the CornerRadius property.
        var button = new Button { Content = "x" };
        var window = new Window { Content = button };
        window.Show();

        App.RequestedThemeVariant = BirkoThemeVariants.Light;
        button.CornerRadius.Should().Be(new CornerRadius(6));

        App.RequestedThemeVariant = BirkoThemeVariants.Finstat; // flat / square
        button.CornerRadius.Should().Be(new CornerRadius(0));
    }

    [AvaloniaFact]
    public void TextBox_surface_and_text_tokens_reskin_per_theme()
    {
        var box = new TextBox { Text = "hello" };
        var window = new Window { Content = box };
        window.Show();

        App.RequestedThemeVariant = BirkoThemeVariants.Light;
        BackgroundColor(box).Should().Be(Color.Parse("#FFFFFF")); // BBg light
        (box.Foreground as ISolidColorBrush)!.Color.Should().Be(Color.Parse("#0F172A")); // BText light

        App.RequestedThemeVariant = BirkoThemeVariants.Dark;
        BackgroundColor(box).Should().Be(Color.Parse("#0F172A")); // BBg dark
        (box.Foreground as ISolidColorBrush)!.Color.Should().Be(Color.Parse("#F1F5F9")); // BText dark
    }

    [AvaloniaFact]
    public void Card_theme_applies_elevated_surface()
    {
        var card = new ContentControl { Theme = (ControlTheme)App.FindResource("BCard")!, Content = "body" };
        var window = new Window { Content = card };
        window.Show();

        App.RequestedThemeVariant = BirkoThemeVariants.Light;
        BackgroundColor(card).Should().Be(Color.Parse("#FFFFFF")); // BBgElevated light
    }

    [AvaloniaFact]
    public void Badge_theme_resolves_and_is_findable()
    {
        App.TryFindResource("BBadge", out var theme).Should().BeTrue();
        theme.Should().BeOfType<ControlTheme>();
    }
}
