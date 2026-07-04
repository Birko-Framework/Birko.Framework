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

    private static readonly string Tokax = AxamlEmitter.Generate(Tokens)["Tokens.axaml"];
    private static readonly XDocument Doc = XDocument.Parse(Tokax);

    [Fact]
    public void Emits_a_single_tokens_file() =>
        AxamlEmitter.Generate(Tokens).Keys.Should().BeEquivalentTo(new[] { "Tokens.axaml" });

    [Fact]
    public void Is_well_formed_xml()
    {
        Action parse = () => XDocument.Parse(Tokax);
        parse.Should().NotThrow();
    }

    [Fact]
    public void Has_a_theme_dictionary_per_variant()
    {
        VariantDicts().Should().HaveCount(4, "light, dark, neon, finstat");
        // Built-ins via {x:Static ...Light/Dark}; custom via {x:Static ...Neon/Finstat}.
        foreach (var v in new[] { "Light", "Dark", "Neon", "Finstat" })
            VariantDict(v).Should().NotBeNull($"a ThemeDictionaries entry for {v} must exist");
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
        var sets = new[] { "Light", "Dark", "Neon", "Finstat" }.Select(v => KeysIn(VariantDict(v)!)).ToList();
        foreach (var set in sets)
            set.Should().BeEquivalentTo(sets[0], "every variant must expose the same keys (swap safety)");
    }

    [Fact]
    public void Every_color_has_a_root_brush_linked_by_dynamic_resource()
    {
        var colorKeys = KeysOfElements(VariantDict("Light")!, "Color");
        var rootBrushes = Doc.Root!.Elements()
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
    [InlineData("--b-color-primary", "BColorPrimary")]
    [InlineData("--b-bg", "BBg")]
    [InlineData("--b-space-2xs", "BSpace2xs")]
    public void ToKey_pascal_cases_token_names(string varName, string key) =>
        AxamlEmitter.ToKey(varName).Should().Be(key);

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static IEnumerable<XElement> VariantDicts() =>
        Doc.Descendants().First(e => e.Name.LocalName == "ResourceDictionary.ThemeDictionaries")
            .Elements().Where(e => e.Name.LocalName == "ResourceDictionary");

    private static XElement? VariantDict(string variant) =>
        VariantDicts().FirstOrDefault(e => KeyOf(e).Contains(variant, StringComparison.Ordinal));

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
