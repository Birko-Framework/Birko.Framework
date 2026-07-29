using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Core.Theming;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// The per-theme AXAML split: a consumer merges only the themes it offers. These tests pin the
/// Avalonia resource-resolution behaviour the split relies on, then verify the real generated files
/// and the theme detection built on them.
/// </summary>
public class ThemeCompositionTests
{
    private const string ThemeIdKey = "BThemeId";

    private static ResourceDictionary ThemeOnly(ThemeVariant variant, Color color)
    {
        var dict = new ResourceDictionary();
        dict.ThemeDictionaries[variant] = new ResourceDictionary { ["K"] = color };
        return dict;
    }

    private static ResourceDictionary Include(params string[] files)
    {
        var outer = new ResourceDictionary();
        foreach (var file in files)
            outer.MergedDictionaries.Add(new ResourceInclude((Uri?)null)
            {
                Source = new Uri($"avares://Birko.Xaml.Avalonia/{file}"),
            });
        return outer;
    }

    // ── The mechanism the split depends on ──────────────────────────────────

    [AvaloniaFact]
    public void Theme_dictionaries_in_merged_files_resolve_per_variant()
    {
        // Without this, themes could not live in separate files at all.
        var outer = new ResourceDictionary();
        outer.MergedDictionaries.Add(ThemeOnly(ThemeVariant.Light, Colors.Red));
        outer.MergedDictionaries.Add(ThemeOnly(BirkoThemeVariants.Neon, Colors.Lime));

        outer.TryGetResource("K", ThemeVariant.Light, out var light).Should().BeTrue();
        light.Should().Be(Colors.Red);
        outer.TryGetResource("K", BirkoThemeVariants.Neon, out var neon).Should().BeTrue();
        neon.Should().Be(Colors.Lime);
    }

    [AvaloniaFact]
    public void An_omitted_custom_variant_degrades_to_its_inherit_base()
    {
        // Why Neon/Finstat are safely omissible: they inherit Dark/Light rather than failing.
        var outer = new ResourceDictionary();
        outer.MergedDictionaries.Add(ThemeOnly(ThemeVariant.Dark, Colors.Blue));

        outer.TryGetResource("K", BirkoThemeVariants.Neon, out var neon).Should().BeTrue();
        neon.Should().Be(Colors.Blue);
    }

    [AvaloniaFact]
    public void Dark_does_not_degrade_to_light_which_is_why_core_ships_both()
    {
        // ThemeVariant.Dark has no InheritVariant: a light-only app resolves NOTHING under OS dark
        // mode. This is the reason BirkoTheme.Core.axaml includes Dark rather than Light alone.
        var outer = new ResourceDictionary();
        outer.MergedDictionaries.Add(ThemeOnly(ThemeVariant.Light, Colors.Red));

        outer.TryGetResource("K", ThemeVariant.Dark, out var dark).Should().BeFalse();
        dark.Should().BeNull();
    }

    // ── The real generated files ────────────────────────────────────────────

    [AvaloniaFact]
    public void Core_include_ships_light_and_dark_but_not_neon_or_finstat()
    {
        var core = Include("BirkoTheme.Core.axaml");

        core.TryGetResource(ThemeIdKey, BirkoThemeVariants.Light, out var light).Should().BeTrue();
        light.Should().Be("light");
        core.TryGetResource(ThemeIdKey, BirkoThemeVariants.Dark, out var dark).Should().BeTrue();
        dark.Should().Be("dark");

        // Neon/Finstat are not shipped — the lookup is answered by the inherited base, and says so.
        core.TryGetResource(ThemeIdKey, BirkoThemeVariants.Neon, out var neon).Should().BeTrue();
        neon.Should().Be("dark", "Neon was not merged, so its Dark base answered");
        core.TryGetResource(ThemeIdKey, BirkoThemeVariants.Finstat, out var finstat).Should().BeTrue();
        finstat.Should().Be("light", "Finstat was not merged, so its Light base answered");
    }

    [AvaloniaFact]
    public void Core_include_still_resolves_colors_and_brushes()
    {
        var core = Include("BirkoTheme.Core.axaml");

        core.TryGetResource("BColorPrimary", BirkoThemeVariants.Dark, out var color).Should().BeTrue();
        color.Should().Be(Color.Parse("#3B82F6"));

        // The shared brush sheet must come along, or every themed brush key breaks.
        core.TryGetResource("BColorPrimaryBrush", BirkoThemeVariants.Dark, out var brush).Should().BeTrue();
        brush.Should().BeAssignableTo<ISolidColorBrush>();
    }

    [AvaloniaFact]
    public void Adding_one_theme_file_to_core_offers_exactly_that_theme()
    {
        var core = Include("BirkoTheme.Core.axaml", "Themes/Tokens.Neon.axaml");

        core.TryGetResource(ThemeIdKey, BirkoThemeVariants.Neon, out var neon).Should().BeTrue();
        neon.Should().Be("neon", "the Neon dictionary is now merged and answers for itself");
        core.TryGetResource("BColorPrimary", BirkoThemeVariants.Neon, out var color).Should().BeTrue();
        color.Should().Be(Color.Parse("#8CFFB0"));

        // Finstat was still not merged.
        core.TryGetResource(ThemeIdKey, BirkoThemeVariants.Finstat, out var finstat).Should().BeTrue();
        finstat.Should().Be("light");
    }

    [AvaloniaFact]
    public void Aggregate_include_offers_all_four()
    {
        var all = Include("BirkoTheme.axaml");

        foreach (var (variant, id) in new (ThemeVariant, string)[]
                 {
                     (BirkoThemeVariants.Light, "light"),
                     (BirkoThemeVariants.Dark, "dark"),
                     (BirkoThemeVariants.Neon, "neon"),
                     (BirkoThemeVariants.Finstat, "finstat"),
                 })
        {
            all.TryGetResource(ThemeIdKey, variant, out var answered).Should().BeTrue();
            answered.Should().Be(id, $"{id} must be shipped by the all-in bundle");
        }
    }

    // ── Detection built on the sentinel ─────────────────────────────────────
    // Deliberately probing a bare ResourceDictionary rather than a second Application: constructing
    // an Application inside a test disturbs the ambient headless one that the rest of the suite
    // shares, which silently broke an unrelated test.

    [AvaloniaFact]
    public void Detection_reports_only_the_themes_that_were_merged()
    {
        AvaloniaThemeManager.DetectThemes(Include("BirkoTheme.Core.axaml"))
            .Select(t => t.Id).Should().BeEquivalentTo(new[] { "light", "dark" });

        AvaloniaThemeManager.DetectThemes(Include("BirkoTheme.Core.axaml", "Themes/Tokens.Neon.axaml"))
            .Select(t => t.Id).Should().BeEquivalentTo(new[] { "light", "dark", "neon" });

        AvaloniaThemeManager.DetectThemes(Include("BirkoTheme.axaml"))
            .Select(t => t.Id).Should().BeEquivalentTo(new[] { "light", "dark", "neon", "finstat" });
    }

    [AvaloniaFact]
    public void Detection_falls_back_to_light_when_no_birko_tokens_are_merged() =>
        AvaloniaThemeManager.DetectThemes(new ResourceDictionary())
            .Select(t => t.Id).Should().BeEquivalentTo(new[] { "light" });

    [AvaloniaFact]
    public void Set_theme_refuses_a_theme_that_was_not_shipped()
    {
        // Explicit available list — the switcher must not act on a theme whose tokens are absent.
        var mgr = new AvaloniaThemeManager(
            Application.Current,
            new[] { BirkoThemes.LightTheme, BirkoThemes.DarkTheme });

        mgr.SetTheme("neon").Should().BeFalse("neon is not in the available set");
        mgr.SetTheme("dark").Should().BeTrue();
    }
}
