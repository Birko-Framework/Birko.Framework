using System.Globalization;
using Birko.DesignTokens;
using FluentAssertions;
using Xunit;

namespace Birko.DesignTokens.Tests;

/// <summary>
/// WCAG 2.1 AA text contrast (4.5:1) for token pairs that components actually render as text on a
/// surface. Measured from tokens.json, so a value change that drops a pair below AA fails here before
/// it ships. `--b-text-muted` sat at 2.45–3.75:1 in light and dark for its whole life — 119 component
/// rules, the shared modal/drawer close button among them — until Presenter's axe audit (TASK-562).
/// </summary>
public class ContrastTests
{
    private const double AaText = 4.5;

    private static readonly TokenSet Tokens = TokenSet.Load(Paths.Resolve(Array.Empty<string>()).TokensJson);

    public static IEnumerable<object[]> Pairs => new[]
    {
        new object[] { "light", "--b-text-muted", "--b-bg" },
        new object[] { "light", "--b-text-muted", "--b-bg-secondary" },
        new object[] { "light", "--b-text-on-primary", "--b-color-primary" },
        new object[] { "light", "--b-text-on-primary", "--b-color-primary-hover" },
        new object[] { "dark", "--b-text-muted", "--b-bg" },
        new object[] { "dark", "--b-text-muted", "--b-bg-secondary" },
        new object[] { "dark", "--b-text-on-primary", "--b-color-primary" },
        new object[] { "dark", "--b-text-on-primary", "--b-color-primary-hover" },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Text_token_meets_AA_on_its_surface(string theme, string text, string surface)
    {
        double ratio = Ratio(Resolve(theme, text), Resolve(theme, surface));

        ratio.Should().BeGreaterThanOrEqualTo(AaText, $"{text} on {surface} in {theme} is text, and AA needs 4.5:1");
    }

    [Fact]
    public void Ratio_matches_the_WCAG_reference_values()
    {
        Ratio("#000000", "#ffffff").Should().BeApproximately(21.0, 0.01);
        Ratio("#64748b", "#1e293b").Should().BeApproximately(3.07, 0.01);
    }

    /// <summary>A theme sheet only overrides; anything it leaves out, and every var(), resolves via :root.</summary>
    private static string Resolve(string theme, string name)
    {
        var root = Tokens.Sheets.Single(s => s.Selector == ":root");
        var sheet = Tokens.Sheets.Single(s => s.Theme == theme);
        string? value = Find(sheet, name) ?? Find(root, name);
        value.Should().NotBeNull($"{name} must be defined for {theme}");
        while (value!.StartsWith("var(", StringComparison.Ordinal))
        {
            string inner = value[4..^1].Split(',')[0].Trim();
            value = Find(sheet, inner) ?? Find(root, inner)
                ?? throw new InvalidOperationException($"{inner} (from {name}) is undefined in {theme}");
        }
        return value;
    }

    private static string? Find(Sheet sheet, string name) =>
        sheet.Body.LastOrDefault(n => n.IsVar && n.Name == name)?.Value?.Trim();

    private static double Ratio(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        hex.Should().MatchRegex("^#[0-9a-fA-F]{6}$", "contrast is measured on opaque 6-digit hex colours");
        double Channel(int i)
        {
            double c = int.Parse(hex.AsSpan(1 + 2 * i, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2);
    }
}
