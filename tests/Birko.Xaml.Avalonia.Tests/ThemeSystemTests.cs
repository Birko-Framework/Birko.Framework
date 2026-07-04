using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Core.Theming;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// STORY-030 proof: the generated Tokens.axaml loads, all four variants resolve their tokens,
/// and a runtime RequestedThemeVariant swap re-resolves DynamicResource references live.
/// </summary>
public class ThemeSystemTests
{
    private static Application App => Application.Current!;

    private static Color Resolve(string key, ThemeVariant variant)
    {
        App.TryGetResource(key, variant, out var value).Should().BeTrue($"{key} must exist for {variant.Key}");
        return value switch
        {
            Color c => c,
            ISolidColorBrush b => b.Color,
            _ => throw new Xunit.Sdk.XunitException($"{key} is not a color ({value?.GetType().Name ?? "null"})"),
        };
    }

    [AvaloniaFact]
    public void Tokens_axaml_loads_and_light_resolves()
    {
        App.Resources.MergedDictionaries.Should().NotBeEmpty();
        Resolve("BColorPrimary", BirkoThemeVariants.Light).Should().Be(Color.Parse("#2563EB"));
    }

    [AvaloniaFact]
    public void Primary_color_resolves_per_variant()
    {
        Resolve("BColorPrimary", BirkoThemeVariants.Light).Should().Be(Color.Parse("#2563EB"));
        Resolve("BColorPrimary", BirkoThemeVariants.Dark).Should().Be(Color.Parse("#3B82F6"));
        Resolve("BColorPrimary", BirkoThemeVariants.Neon).Should().Be(Color.Parse("#8CFFB0"));
        Resolve("BColorPrimary", BirkoThemeVariants.Finstat).Should().Be(Color.Parse("#25BA7A"));
    }

    [AvaloniaFact]
    public void Var_reference_token_resolves_to_active_variant()
    {
        // --b-border-focus: var(--b-color-primary) — tracks each theme's primary.
        Resolve("BBorderFocus", BirkoThemeVariants.Light).Should().Be(Color.Parse("#2563EB"));
        Resolve("BBorderFocus", BirkoThemeVariants.Dark).Should().Be(Color.Parse("#3B82F6"));
    }

    [AvaloniaFact]
    public void Length_token_is_baked_from_rem()
    {
        App.TryGetResource("BSpaceMd", BirkoThemeVariants.Light, out var space).Should().BeTrue();
        space.Should().Be(12d); // 0.75rem x 16
    }

    [AvaloniaFact]
    public void Radius_is_a_corner_radius_and_finstat_flattens_it()
    {
        App.TryGetResource("BRadius", BirkoThemeVariants.Light, out var light).Should().BeTrue();
        light.Should().Be(new global::Avalonia.CornerRadius(6)); // 0.375rem x 16

        App.TryGetResource("BRadius", BirkoThemeVariants.Finstat, out var fin).Should().BeTrue();
        fin.Should().Be(new global::Avalonia.CornerRadius(0));   // finstat is flat / square
    }

    [AvaloniaFact]
    public void Runtime_variant_swap_reresolves_dynamic_resource_brush_live()
    {
        var border = new Border();
        // Background follows the themed brush via DynamicResource.
        border.Bind(Border.BackgroundProperty, App.GetResourceObservable("BColorPrimaryBrush"));
        var window = new Window { Content = border };
        window.Show();

        App.RequestedThemeVariant = BirkoThemeVariants.Light;
        var light = ((ISolidColorBrush)border.Background!).Color;

        App.RequestedThemeVariant = BirkoThemeVariants.Neon;
        var neon = ((ISolidColorBrush)border.Background!).Color;

        App.RequestedThemeVariant = BirkoThemeVariants.Finstat;
        var finstat = ((ISolidColorBrush)border.Background!).Color;

        light.Should().Be(Color.Parse("#2563EB"));
        neon.Should().Be(Color.Parse("#8CFFB0"));
        finstat.Should().Be(Color.Parse("#25BA7A"));
        light.Should().NotBe(neon, "the swap must actually change the resolved color");
    }
}

public class ThemeManagerTests
{
    [AvaloniaFact]
    public void Manager_lists_all_four_themes()
    {
        var mgr = new AvaloniaThemeManager();
        mgr.Available.Select(t => t.Id)
            .Should().BeEquivalentTo(new[] { "light", "dark", "neon", "finstat" });
    }

    [AvaloniaFact]
    public void SetTheme_switches_active_variant_and_current()
    {
        var mgr = new AvaloniaThemeManager();
        ThemeInfo? raised = null;
        mgr.ThemeChanged += t => raised = t;

        mgr.SetTheme("neon").Should().BeTrue();

        Application.Current!.RequestedThemeVariant.Should().Be(BirkoThemeVariants.Neon);
        mgr.Current.Id.Should().Be("neon");
        raised!.Id.Should().Be("neon");
    }

    [AvaloniaFact]
    public void SetTheme_ignores_unknown_id()
    {
        var mgr = new AvaloniaThemeManager();
        mgr.SetTheme("nope").Should().BeFalse();
    }
}
