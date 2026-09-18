using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Birko.DesignTokens;

/// <summary>
/// Emits the Avalonia token dictionaries from the token model, using
/// <c>ResourceDictionary.ThemeDictionaries</c> so a runtime <c>RequestedThemeVariant</c> swap
/// re-resolves every <c>{DynamicResource}</c> reference live (STORY-030).
///
/// Layout — <b>one file per theme</b>, so a consumer ships only the themes it offers (mirroring
/// the web side, where each alternate theme is its own opt-in CSS file):
///  - <c>Tokens.{Light,Dark,Neon,Finstat}.axaml</c> — one ThemeDictionaries entry each, keyed by the
///    matching <c>BirkoThemeVariants</c> static (via <c>{x:Static}</c>), holding that theme's
///    resolved Colors + lengths (rem baked to px) + numerics + FontFamilies. Each is complete and
///    self-contained (theme override ?? light base), so any subset can be merged.
///  - <c>Tokens.Brushes.axaml</c> — brushes declared ONCE, each linking its Color via
///    <c>{DynamicResource}</c>, so a variant swap updates the brush without duplicating it per
///    theme. Theme-independent, hence shared by every subset.
///  - <c>Tokens.axaml</c> — back-compat aggregate merging the brushes + all four themes, so
///    existing <c>BirkoTheme.axaml</c> consumers are unaffected.
///
/// Merging a subset works because Avalonia resolves ThemeDictionaries entries found in *merged*
/// dictionaries, and an omitted custom variant degrades to its <c>InheritVariant</c> base rather
/// than failing (both verified in <c>Birko.Xaml.Avalonia.Tests</c> ThemeCompositionTests).
///
/// STORY-029 covers the unambiguous, high-value tokens; composite/motion tokens (shadows, focus
/// rings, transitions, cubic-bezier easings, durations, gradients) remain deferred.
/// </summary>
public static class AxamlEmitter
{
    private const string ThemesNamespace = "clr-namespace:Birko.Xaml.Avalonia.Theming;assembly=Birko.Xaml.Avalonia";
    private const string VariantsClass = "BirkoThemeVariants";
    private const string ThemesUri = "avares://Birko.Xaml.Avalonia/Themes";

    /// <summary>Resource key whose value NAMES the theme dictionary that answered a lookup.
    /// Presence alone cannot reveal which themes are loaded — an omitted custom variant silently
    /// inherits its base (Neon→Dark) — so a sentinel that names its own theme is the only reliable
    /// discriminator. <c>AvaloniaThemeManager</c> uses it to derive the switcher list from what was
    /// actually merged, keeping one source of truth instead of a second hand-maintained list.</summary>
    public const string ThemeIdKey = "BThemeId";

    /// <summary>Shared root-level brushes; required alongside any theme subset.</summary>
    public const string BrushesFile = "Tokens.Brushes.axaml";

    /// <summary>Back-compat aggregate: brushes + all four themes.</summary>
    public const string AggregateFile = "Tokens.axaml";

    /// <summary>The per-theme dictionary file for a <c>BirkoThemeVariants</c> member name.</summary>
    public static string ThemeFile(string variant) => $"Tokens.{variant}.axaml";

    private static readonly string[] FontNames = { "--b-font", "--b-font-heading", "--b-font-mono" };

    /// <summary>Themes emitted to AXAML (the epic's Light/Dark/Neon/Finstat) and the
    /// <c>BirkoThemeVariants</c> member each maps to. The CSS-only scoped "inverse" partial is
    /// not emitted here — revisit if desktop ever needs a scoped dark-chrome variant.</summary>
    public static readonly (string Theme, string Variant)[] AxamlThemes =
    {
        ("light", "Light"),
        ("dark", "Dark"),
        ("neon", "Neon"),
        ("finstat", "Finstat"),
    };

    /// <summary>Generate the AXAML files. Returns file name → content: one dictionary per theme,
    /// the shared brush sheet, and the back-compat aggregate.</summary>
    public static Dictionary<string, string> Generate(TokenSet tokens)
    {
        var light = VarMap(tokens, "light");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (theme, variant) in AxamlThemes)
            files[ThemeFile(variant)] = ThemeSheet(theme, variant, VarMap(tokens, theme), light);

        files[BrushesFile] = BrushesSheet(light);
        files[AggregateFile] = AggregateSheet();
        return files;
    }

    /// <summary>One theme's ThemeDictionaries entry as a standalone, mergeable dictionary.</summary>
    private static string ThemeSheet(
        string theme, string variant,
        Dictionary<string, string> themeVars, Dictionary<string, string> light)
    {
        var sb = new StringBuilder();
        Header(sb,
            $"The '{theme}' design tokens as one Avalonia ThemeDictionaries entry. Merge this file to",
            "offer the theme; omit it and the variant degrades to its InheritVariant base instead.",
            "Swap at runtime via RequestedThemeVariant (see AvaloniaThemeManager) — DynamicResource",
            "references re-resolve live. Needs Tokens.Brushes.axaml merged alongside it.");
        sb.Append("<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"\r\n");
        sb.Append("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\r\n");
        sb.Append($"                    xmlns:themes=\"{ThemesNamespace}\">\r\n\r\n");

        sb.Append("  <ResourceDictionary.ThemeDictionaries>\r\n");
        EmitVariantDictionary(sb, theme, variant, themeVars, light);
        sb.Append("  </ResourceDictionary.ThemeDictionaries>\r\n\r\n");

        sb.Append("</ResourceDictionary>\r\n");
        return sb.ToString();
    }

    /// <summary>The shared brush sheet — theme-independent, so every subset merges this one file.</summary>
    private static string BrushesSheet(Dictionary<string, string> light)
    {
        var sb = new StringBuilder();
        Header(sb,
            "Brushes for every color token — one instance each, Color supplied by DynamicResource so",
            "it tracks the active ThemeVariant. Theme-independent: merge alongside ANY theme subset.");
        sb.Append("<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"\r\n");
        sb.Append("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\r\n\r\n");
        EmitBrushes(sb, light);
        sb.Append("</ResourceDictionary>\r\n");
        return sb.ToString();
    }

    /// <summary>Back-compat aggregate: brushes + all four themes, which is what
    /// <c>BirkoTheme.axaml</c> has always pulled in.</summary>
    private static string AggregateSheet()
    {
        var sb = new StringBuilder();
        Header(sb,
            "Every Birko theme (light/dark/neon/finstat) plus the shared brushes — the all-in bundle.",
            "To ship only some themes, merge Tokens.Brushes.axaml plus the Tokens.<Theme>.axaml files",
            "you want instead of this one (BirkoTheme.Core.axaml does exactly that for light+dark).");
        sb.Append("<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"\r\n");
        sb.Append("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\r\n\r\n");
        sb.Append("  <ResourceDictionary.MergedDictionaries>\r\n");
        sb.Append($"    <ResourceInclude Source=\"{ThemesUri}/{BrushesFile}\" />\r\n");
        foreach (var (_, variant) in AxamlThemes)
            sb.Append($"    <ResourceInclude Source=\"{ThemesUri}/{ThemeFile(variant)}\" />\r\n");
        sb.Append("  </ResourceDictionary.MergedDictionaries>\r\n\r\n");
        sb.Append("</ResourceDictionary>\r\n");
        return sb.ToString();
    }

    private static void Header(StringBuilder sb, params string[] lines)
    {
        sb.Append("<!--\r\n");
        sb.Append("  AUTO-GENERATED by Birko.DesignTokens from tokens.json. DO NOT EDIT.\r\n");
        foreach (var line in lines) sb.Append("  ").Append(line).Append("\r\n");
        sb.Append("  Regenerate with the Birko.DesignTokens 'generate' command.\r\n");
        sb.Append("-->\r\n");
    }

    /// <summary>All var name→value declarations for a theme, in body order.</summary>
    private static Dictionary<string, string> VarMap(TokenSet tokens, string theme)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var sheet = tokens.Sheets.FirstOrDefault(s => s.Theme == theme);
        if (sheet is null) return map;
        foreach (var node in sheet.Body)
            if (node.IsVar && node.Name is not null && node.Value is not null)
                map[node.Name] = node.Value;
        return map;
    }

    private static void EmitVariantDictionary(
        StringBuilder sb, string theme, string variant,
        Dictionary<string, string> themeVars, Dictionary<string, string> light)
    {
        // Resolve every token as (theme override ?? light base) so the dictionary is complete.
        var resolved = new Dictionary<string, string>(light, StringComparer.Ordinal);
        foreach (var kv in themeVars) resolved[kv.Key] = kv.Value;

        var colors = new StringBuilder();
        var radii = new StringBuilder();
        var lengths = new StringBuilder();
        var numbers = new StringBuilder();
        var fonts = new StringBuilder();
        int skipped = 0;

        foreach (var name in resolved.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            string key = ToKey(name);
            string value = Resolve(resolved[name], resolved);

            if (FontNames.Contains(name))
                fonts.Append($"      <FontFamily x:Key=\"{key}\">{XmlText(value)}</FontFamily>\r\n");
            else if (TryColor(value, out string hex))
                colors.Append($"      <Color x:Key=\"{key}\">{hex}</Color>\r\n");
            else if (IsRadius(name) && TryLengthToPx(value, out double r))
                // Radius tokens are consumed as CornerRadius in XAML — emit the right type so a
                // {DynamicResource} binds directly (a raw x:Double can't cast to CornerRadius).
                radii.Append($"      <CornerRadius x:Key=\"{key}\">{Num(r)}</CornerRadius>\r\n");
            else if (TryLengthToPx(value, out double px))
                lengths.Append($"      <x:Double x:Key=\"{key}\">{Num(px)}</x:Double>\r\n");
            else if (IsSimpleNumeric(name, value, out double n))
                numbers.Append($"      <x:Double x:Key=\"{key}\">{Num(n)}</x:Double>\r\n");
            else
                skipped++;
        }

        sb.Append($"    <ResourceDictionary x:Key=\"{{x:Static themes:{VariantsClass}.{variant}}}\">\r\n");
        sb.Append("      <!-- Names the dictionary that answered a lookup, so the loaded themes can be\r\n");
        sb.Append("           detected rather than hand-listed (an omitted variant inherits silently). -->\r\n");
        sb.Append($"      <x:String x:Key=\"{ThemeIdKey}\">{theme}</x:String>\r\n");
        AppendSection(sb, "Colors", colors);
        AppendSection(sb, "Corner radii", radii);
        AppendSection(sb, "Lengths (px — baked from rem x 16)", lengths);
        AppendSection(sb, "Numerics (font-weight / line-height / opacity / z-index)", numbers);
        AppendSection(sb, "Fonts", fonts);
        sb.Append($"      <!-- {skipped} composite/motion token(s) deferred (shadows, focus rings,\r\n");
        sb.Append("           transitions, easings, durations, gradients). -->\r\n");
        sb.Append("    </ResourceDictionary>\r\n");
    }

    /// <summary>Root-level brushes: one per color token, colour supplied by DynamicResource so it
    /// tracks the active variant. Keyed off the light base (the canonical color-key set).</summary>
    private static void EmitBrushes(StringBuilder sb, Dictionary<string, string> light)
    {
        var brushes = new StringBuilder();
        foreach (var name in light.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!TryColor(Resolve(light[name], light), out _)) continue;
            string key = ToKey(name);
            brushes.Append($"  <SolidColorBrush x:Key=\"{key}Brush\" Color=\"{{DynamicResource {key}}}\" />\r\n");
        }

        sb.Append("  <!-- Brushes — one per color token; Color re-resolves per active ThemeVariant -->\r\n");
        sb.Append(brushes);
        sb.Append('\r').Append('\n');
    }

    private static void AppendSection(StringBuilder sb, string title, StringBuilder body)
    {
        if (body.Length == 0) return;
        sb.Append($"      <!-- {title} -->\r\n");
        sb.Append(body);
    }

    // ── Resolution ─────────────────────────────────────────────────────────
    private static readonly Regex VarRef = new(@"^var\((--[A-Za-z0-9-]+)\)$", RegexOptions.Compiled);

    /// <summary>Resolve a pure <c>var(--x)</c> reference to its underlying value within the same
    /// theme (recursively). Non-pure-ref values (shorthands) are returned unchanged.</summary>
    private static string Resolve(string value, Dictionary<string, string> map, int depth = 0)
    {
        if (depth > 16) return value;
        var m = VarRef.Match(value.Trim());
        if (m.Success && map.TryGetValue(m.Groups[1].Value, out var inner))
            return Resolve(inner, map, depth + 1);
        return value;
    }

    // ── Colors ─────────────────────────────────────────────────────────────
    private static readonly Regex Hex = new(@"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);
    private static readonly Regex Rgb = new(@"^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)$", RegexOptions.Compiled);

    /// <summary>True if the value is a color; outputs Avalonia hex (#RRGGBB or #AARRGGBB).</summary>
    internal static bool TryColor(string value, out string hex)
    {
        hex = string.Empty;
        value = value.Trim();

        var h = Hex.Match(value);
        if (h.Success)
        {
            string body = value.Substring(1);
            if (body.Length == 3) // #rgb -> #rrggbb
                body = string.Concat(body.Select(c => new string(c, 2)));
            if (body.Length == 8) // CSS #rrggbbaa -> Avalonia #aarrggbb
                body = body.Substring(6, 2) + body.Substring(0, 6);
            hex = "#" + body.ToUpperInvariant();
            return true;
        }

        var r = Rgb.Match(value);
        if (r.Success)
        {
            int ri = int.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture);
            int gi = int.Parse(r.Groups[2].Value, CultureInfo.InvariantCulture);
            int bi = int.Parse(r.Groups[3].Value, CultureInfo.InvariantCulture);
            if (r.Groups[4].Success)
            {
                double a = double.Parse(r.Groups[4].Value, CultureInfo.InvariantCulture);
                int ai = (int)Math.Round(Math.Clamp(a, 0, 1) * 255);
                hex = $"#{ai:X2}{ri:X2}{gi:X2}{bi:X2}";
            }
            else
            {
                hex = $"#{ri:X2}{gi:X2}{bi:X2}";
            }
            return true;
        }

        return false;
    }

    // ── Lengths ────────────────────────────────────────────────────────────
    private static readonly Regex RemPx = new(@"^(-?[\d.]+)(rem|px)$", RegexOptions.Compiled);

    /// <summary>True if the value is a single rem/px length; outputs px (rem baked at 16px root).</summary>
    internal static bool TryLengthToPx(string value, out double px)
    {
        px = 0;
        value = value.Trim();
        // CSS allows a unitless zero for lengths (e.g. finstat's --b-radius: 0). Treat it as 0px so
        // a theme that flattens a dimension keeps the same resource keys as the others.
        if (value == "0") return true;
        var m = RemPx.Match(value);
        if (!m.Success) return false;
        double n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        px = m.Groups[2].Value == "rem" ? n * 16 : n;
        return true;
    }

    /// <summary>Radius tokens map to Avalonia <c>CornerRadius</c> (their XAML consumption type).</summary>
    private static bool IsRadius(string name) => name.StartsWith("--b-radius", StringComparison.Ordinal);

    // ── Simple numerics ──────────────────────────────────────────────────────
    private static bool IsSimpleNumeric(string name, string value, out double n)
    {
        n = 0;
        bool eligible = name.StartsWith("--b-font-weight", StringComparison.Ordinal)
                        || name.StartsWith("--b-line-height", StringComparison.Ordinal)
                        || name.EndsWith("-opacity", StringComparison.Ordinal)
                        || name.StartsWith("--b-z-", StringComparison.Ordinal);
        if (!eligible) return false;
        return double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out n);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static string Num(double d) => d.ToString("0.############", CultureInfo.InvariantCulture);

    /// <summary>--b-color-primary -> BColorPrimary.</summary>
    internal static string ToKey(string varName)
    {
        var parts = varName.TrimStart('-').Split('-', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var p in parts)
            sb.Append(char.ToUpperInvariant(p[0])).Append(p.AsSpan(1));
        return sb.ToString();
    }

    private static string XmlText(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
