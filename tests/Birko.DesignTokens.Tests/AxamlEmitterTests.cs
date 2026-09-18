using System.Xml.Linq;
using Birko.DesignTokens;
using FluentAssertions;
using Xunit;

namespace Birko.DesignTokens.Tests;

public class AxamlEmitterTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly TokenSet Tokens =
        TokenSet.Load(Paths.Resolve(Array.Empty<string>()).TokensJson);

    private static readonly Dictionary<string, string> Files = AxamlEmitter.Generate(Tokens);

    private static readonly string[] Variants = { "Light", "Dark", "Neon", "Finstat" };

    [Fact]
    public void Emits_one_file_per_theme_plus_brushes_and_a_back_compat_aggregate() =>
        Files.Keys.Should().BeEquivalentTo(new[]
        {
            "Tokens.Light.axaml", "Tokens.Dark.axaml", "Tokens.Neon.axaml", "Tokens.Finstat.axaml",
            AxamlEmitter.BrushesFile, AxamlEmitter.AggregateFile,
        });

    [Fact]
    public void Every_emitted_file_is_well_formed_xml()
    {
        foreach (var (name, content) in Files)
        {
            Action parse = () => XDocument.Parse(content);
            parse.Should().NotThrow($"{name} must be well-formed");
        }
    }

    [Fact]
    public void Each_theme_file_holds_exactly_its_own_variant()
    {
        foreach (var v in Variants)
        {
            var dicts = VariantDicts(v).ToList();
            dicts.Should().HaveCount(1, $"Tokens.{v}.axaml must declare only the {v} entry, so it can be omitted independently");
            KeyOf(dicts[0]).Should().Contain(v);
        }
    }

    [Fact]
    public void Each_theme_file_names_itself_via_the_theme_id_sentinel()
    {
        // Presence cannot reveal which themes are loaded — an omitted variant inherits its base
        // silently — so each dictionary states its own id for AvaloniaThemeManager to detect.
        foreach (var (variant, theme) in Variants.Zip(new[] { "light", "dark", "neon", "finstat" }))
        {
            var sentinel = VariantDict(variant)!.Elements()
                .FirstOrDefault(e => e.Attribute(X + "Key")?.Value == AxamlEmitter.ThemeIdKey);
            sentinel.Should().NotBeNull($"{variant} must declare {AxamlEmitter.ThemeIdKey}");
            sentinel!.Value.Should().Be(theme);
        }
    }

    [Fact]
    public void Theme_files_carry_no_brushes_so_the_shared_sheet_is_the_only_source()
    {
        foreach (var v in Variants)
            XDocument.Parse(Files[$"Tokens.{v}.axaml"]).Descendants()
                .Where(e => e.Name.LocalName == "SolidColorBrush")
                .Should().BeEmpty($"Tokens.{v}.axaml must not duplicate brushes — {AxamlEmitter.BrushesFile} owns them");
    }

    [Fact]
    public void Aggregate_merges_the_brushes_and_all_four_themes()
    {
        var sources = XDocument.Parse(Files[AxamlEmitter.AggregateFile]).Descendants()
            .Where(e => e.Name.LocalName == "ResourceInclude")
            .Select(e => e.Attribute("Source")!.Value.Split('/').Last())
            .ToList();

        sources.Should().BeEquivalentTo(new[]
        {
            AxamlEmitter.BrushesFile,
            "Tokens.Light.axaml", "Tokens.Dark.axaml", "Tokens.Neon.axaml", "Tokens.Finstat.axaml",
        });
    }

    [Fact]
    public void Custom_variants_are_keyed_by_static_extension()
    {
        // Neon/Finstat must reference the BirkoThemeVariants statics so key identity matches
        // the value assigned to RequestedThemeVariant at runtime.
        KeyOf(VariantDict("Neon")!).Should().Be("{x:Static themes:BirkoThemeVariants.Neon}");
        KeyOf(VariantDict("Finstat")!).Should().Be("{x:Static themes:BirkoThemeVariants.Finstat}");
    }

    [Fact]
    public void Primary_color_resolves_per_variant()
    {
        ColorIn("Light", "BColorPrimary").Should().Be("#2563EB");
        ColorIn("Dark", "BColorPrimary").Should().Be("#3B82F6");
        ColorIn("Neon", "BColorPrimary").Should().Be("#8CFFB0");
        ColorIn("Finstat", "BColorPrimary").Should().Be("#25BA7A");
    }

    [Fact]
    public void Var_reference_resolves_to_the_active_variant()
    {
        ColorIn("Light", "BBorderFocus").Should().Be("#2563EB");
        ColorIn("Dark", "BBorderFocus").Should().Be("#3B82F6");
    }

    [Fact]
    public void All_variant_dictionaries_expose_the_same_key_set()
    {
        var sets = Variants.Select(v => KeysIn(VariantDict(v)!)).ToList();
        foreach (var set in sets)
            set.Should().BeEquivalentTo(sets[0], "every variant must expose the same keys (swap safety)");
    }

    [Fact]
    public void Every_color_has_a_shared_brush_linked_by_dynamic_resource()
    {
        var colorKeys = KeysOfElements(VariantDict("Light")!, "Color");
        var rootBrushes = XDocument.Parse(Files[AxamlEmitter.BrushesFile]).Root!.Elements()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .ToDictionary(e => e.Attribute(X + "Key")!.Value, e => e.Attribute("Color")!.Value);

        foreach (var c in colorKeys)
        {
            rootBrushes.Should().ContainKey(c + "Brush");
            rootBrushes[c + "Brush"].Should().Be($"{{DynamicResource {c}}}",
                "the brush colour must track the active variant via DynamicResource");
        }
    }

    // ── Conversion unit tests ───────────────────────────────────────────────
    [Theory]
    [InlineData("#2563eb", "#2563EB")]
    [InlineData("#fff", "#FFFFFF")]
    [InlineData("rgb(37, 99, 235)", "#2563EB")]
    [InlineData("rgba(220, 38, 38, 0.08)", "#14DC2626")]
    [InlineData("rgba(0, 0, 0, 0.4)", "#66000000")]
    public void TryColor_produces_avalonia_hex(string input, string expected)
    {
        AxamlEmitter.TryColor(input, out var hex).Should().BeTrue();
        hex.Should().Be(expected);
    }

    [Theory]
    [InlineData("0.75rem", 12)]
    [InlineData("1rem", 16)]
    [InlineData("2px", 2)]
    [InlineData("0", 0)]
    [InlineData("9999px", 9999)]
    public void TryLengthToPx_bakes_rem_at_16(string input, double expected)
    {
        AxamlEmitter.TryLengthToPx(input, out var px).Should().BeTrue();
        px.Should().Be(expected);
    }

    [Theory]
    [InlineData("Light", "Tokens.Light.axaml")]
    [InlineData("Neon", "Tokens.Neon.axaml")]
    public void ThemeFile_names_the_per_theme_dictionary(string variant, string file) =>
        // Consumers hard-code these names in their ResourceInclude URIs — renaming is breaking.
        AxamlEmitter.ThemeFile(variant).Should().Be(file);

    [Theory]
    [InlineData("--b-color-primary", "BColorPrimary")]
    [InlineData("--b-bg", "BBg")]
    [InlineData("--b-space-2xs", "BSpace2xs")]
    public void ToKey_pascal_cases_token_names(string varName, string key) =>
        AxamlEmitter.ToKey(varName).Should().Be(key);

    // ── Helpers ──────────────────────────────────────────────────────────────
    /// <summary>The ThemeDictionaries entries declared by that theme's own file.</summary>
    private static IEnumerable<XElement> VariantDicts(string variant) =>
        XDocument.Parse(Files[$"Tokens.{variant}.axaml"]).Descendants()
            .First(e => e.Name.LocalName == "ResourceDictionary.ThemeDictionaries")
            .Elements().Where(e => e.Name.LocalName == "ResourceDictionary");

    private static XElement? VariantDict(string variant) =>
        VariantDicts(variant).FirstOrDefault(e => KeyOf(e).Contains(variant, StringComparison.Ordinal));

    private static string KeyOf(XElement dict) => dict.Attribute(X + "Key")!.Value;

    private static string ColorIn(string variant, string key) =>
        VariantDict(variant)!.Elements()
            .First(e => e.Name.LocalName == "Color" && e.Attribute(X + "Key")?.Value == key).Value;

    private static HashSet<string> KeysIn(XElement dict) =>
        dict.Elements().Select(e => e.Attribute(X + "Key")?.Value).Where(k => k is not null).Select(k => k!).ToHashSet();

    private static HashSet<string> KeysOfElements(XElement dict, string localName) =>
        dict.Elements().Where(e => e.Name.LocalName == localName)
            .Select(e => e.Attribute(X + "Key")!.Value).ToHashSet();
}
