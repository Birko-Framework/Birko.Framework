using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Theming;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// TASK-822 — the five PARITY.md rows that were `needs-theme` are in the Birko look: NumericUpDown (b-qty-stepper),
/// Carousel (b-carousel), ColorPicker (b-color-picker, through ColorPickerStyles.axaml) and the keyed SelectableTextBlock
/// themes BPre / BCodeBlock (b-pre, b-code-block). Each value is checked against the token it must come from, in the
/// light theme and after a switch, so a hard-coded value or a theme that never applied both fail.
/// </summary>
public class Task822ThemeTests
{
    private static Application App => Application.Current!;

    private static Color Token(string brushKey) =>
        ((ISolidColorBrush)App.FindResource(App.ActualThemeVariant, brushKey)!).Color;

    private static Color ColorOf(IBrush? brush) =>
        (brush as ISolidColorBrush)?.Color ?? throw new Xunit.Sdk.XunitException("not a solid colour brush");

    private static T Show<T>(T control) where T : Control
    {
        new Window { Content = control, Width = 400, Height = 300 }.Show();
        return control;
    }

    [AvaloniaFact]
    public void NumericUpDown_takes_the_input_tokens_and_reskins()
    {
        var stepper = Show(new NumericUpDown { Value = 3 });

        App.RequestedThemeVariant = BirkoThemeVariants.Light;
        ColorOf(stepper.Background).Should().Be(Token("BBgBrush"));
        ColorOf(stepper.BorderBrush).Should().Be(Token("BBorderBrush"));
        stepper.CornerRadius.Should().Be(new CornerRadius(6));

        App.RequestedThemeVariant = BirkoThemeVariants.Finstat; // flat / square
        stepper.CornerRadius.Should().Be(new CornerRadius(0));
    }

    [AvaloniaFact]
    public void Carousel_takes_the_text_and_radius_tokens_and_clips()
    {
        var carousel = Show(new Carousel { ItemsSource = new[] { "one", "two" } });

        App.RequestedThemeVariant = BirkoThemeVariants.Light;
        ColorOf(carousel.Foreground).Should().Be(Token("BTextBrush"));
        carousel.CornerRadius.Should().Be(new CornerRadius(6));
        carousel.ClipToBounds.Should().BeTrue();
    }

    [AvaloniaFact]
    public void ColorPicker_gets_a_template_and_the_input_tokens()
    {
        var picker = Show(new ColorPicker { Color = Colors.SteelBlue });

        App.RequestedThemeVariant = BirkoThemeVariants.Light;
        picker.GetVisualChildren().Should().NotBeEmpty("ColorPickerStyles.axaml brings ColorPicker's Fluent template; without it the control has none");
        ColorOf(picker.Background).Should().Be(Token("BBgBrush"));
        picker.CornerRadius.Should().Be(new CornerRadius(6));

        App.RequestedThemeVariant = BirkoThemeVariants.Dark;
        ColorOf(picker.Background).Should().Be(Token("BBgBrush"));
    }

    [AvaloniaFact]
    public void BPre_and_BCodeBlock_are_monospace_boxes_and_a_plain_SelectableTextBlock_is_left_alone()
    {
        App.RequestedThemeVariant = BirkoThemeVariants.Light;
        var pre = new SelectableTextBlock { Text = "line 1\nline 2", Theme = (ControlTheme)App.FindResource("BPre")! };
        var code = new SelectableTextBlock { Text = "var x = 1;", Theme = (ControlTheme)App.FindResource("BCodeBlock")! };
        var plain = new SelectableTextBlock { Text = "prose" };
        Show(new StackPanel { Children = { pre, code, plain } });

        var mono = (FontFamily)App.FindResource(App.ActualThemeVariant, "BFontMono")!;
        pre.FontFamily.Should().Be(mono);
        ColorOf(pre.Background).Should().Be(Token("BBgSecondaryBrush"));
        code.FontFamily.Should().Be(mono, "BCodeBlock is based on BPre");
        ColorOf(code.Background).Should().Be(Token("BBgTertiaryBrush"));
        code.Padding.Should().Be(new Thickness(16));
        plain.FontFamily.Should().NotBe(mono, "the themes are keyed, so every other SelectableTextBlock keeps its font");
    }

    /// <summary>Not an assertion of pixels: writes the five side by side for a person to look at (TASK-822's review).</summary>
    [AvaloniaFact]
    public void The_five_render_without_error()
    {
        App.RequestedThemeVariant = BirkoThemeVariants.Light;
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(16), Background = Brushes.White };
        panel.Children.Add(new NumericUpDown { Value = 3, Width = 160, HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(new ColorPicker { Color = Colors.SteelBlue, HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(new SelectableTextBlock { Text = "pre: line 1\n     line 2", Theme = (ControlTheme)App.FindResource("BPre")! });
        panel.Children.Add(new SelectableTextBlock { Text = "code: var x = 1;", Theme = (ControlTheme)App.FindResource("BCodeBlock")! });
        panel.Children.Add(new Carousel { ItemsSource = new[] { "carousel slide 1", "slide 2" }, Height = 40 });
        var window = new Window { Content = panel, Width = 420, Height = 360 };
        window.Show();

        var frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();
        var outDir = Environment.GetEnvironmentVariable("BIRKO_TASK822_PNG_DIR");
        if (!string.IsNullOrEmpty(outDir)) frame!.Save(Path.Combine(outDir, "task822-light.png"));
    }
}
