using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Headless.XUnit;
using Birko.Xaml.Avalonia.Theming;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// TASK-763 (FEATURE-019 D13, the desktop side of b-ribbon's TASK-747): every size the ribbon draws is a theme resource,
/// a generated token or one of the ribbon's own sizes in <c>Controls/Ribbon.axaml</c>, never a literal in its code. Read
/// from the source, as the web's check reads its CSS: a literal padding, margin, font size, spacing, width, height,
/// border or radius fails here. A number that is not a drawn size (a platform fallback, a measured constant) says so
/// with <c>size-ok:</c> on its line. Every key the code names must resolve, and every key in the dictionary is used,
/// so a typo cannot silently draw a zero.
/// </summary>
public class RibbonSizeTests
{
    private static string ControlsDir()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Birko.Xaml.Avalonia", "Controls")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test runs inside the framework checkout");
        return System.IO.Path.Combine(dir!.FullName, "Birko.Xaml.Avalonia", "Controls");
    }

    private static IEnumerable<(string File, int Line, string Text)> RibbonCode() =>
        System.IO.Directory.GetFiles(ControlsDir(), "Ribbon*.cs").SelectMany(f =>
            System.IO.File.ReadAllLines(f).Select((t, i) => (System.IO.Path.GetFileName(f), i + 1, t)));

    private static readonly Regex Constructed = new(@"new\s+(Thickness|CornerRadius|Size)\s*\(([^()]*)\)", RegexOptions.Compiled);
    private static readonly Regex Assigned = new(
        @"\b(FontSize|Spacing|LineHeight|Width|Height|MinWidth|MinHeight|MaxWidth|MaxHeight|Padding|Margin|BorderThickness|CornerRadius)\s*=\s*-?\d",
        RegexOptions.Compiled);
    private static readonly Regex Number = new(@"(?<![\w.""])-?\d+(\.\d+)?(?![\w.])", RegexOptions.Compiled);

    [Fact]
    public void No_literal_size_in_the_ribbon_code()
    {
        var offenders = new List<string>();
        foreach (var (file, line, text) in RibbonCode())
        {
            string code = text.Trim();
            if (code.StartsWith("//") || code.StartsWith("///") || code.StartsWith("*") || text.Contains("size-ok:")) continue;
            foreach (Match m in Constructed.Matches(text))
                if (Number.Matches(m.Groups[2].Value).Any(n => n.Value != "0"))
                    offenders.Add($"{file}:{line}: {code}");
            if (Assigned.IsMatch(text)) offenders.Add($"{file}:{line}: {code}");
        }
        offenders.Should().BeEmpty("every size is a theme resource (Controls/Ribbon.axaml or a generated token)");
    }

    [Fact]
    public void The_check_catches_a_literal()
    {
        var samples = new[] { "x.Padding = new Thickness(6, 2);", "var t = new TextBlock { FontSize = 11 };", "Margin = new Thickness(0, 4, 0, 3),", "new CornerRadius(3)" };
        foreach (var s in samples)
            (Constructed.Matches(s).Any(m => Number.Matches(m.Groups[2].Value).Any(n => n.Value != "0")) || Assigned.IsMatch(s))
                .Should().BeTrue($"'{s}' is a literal size");
        foreach (var ok in new[] { "Padding = new Thickness(0),", "new Thickness(0, 0, 0, s.D(\"BBorderWidth\"))", "Sized(x, PaddingProperty, \"BRibbonTabPadding\");" })
            (Constructed.Matches(ok).Any(m => Number.Matches(m.Groups[2].Value).Any(n => n.Value != "0")) || Assigned.IsMatch(ok))
                .Should().BeFalse($"'{ok}' is not");
    }

    [AvaloniaFact]
    public void Every_size_key_the_ribbon_names_resolves_and_every_ribbon_size_is_used()
    {
        var app = Application.Current!;
        app.RequestedThemeVariant = BirkoThemeVariants.Light;
        var named = RibbonCode().SelectMany(c => Regex.Matches(c.Text, @"""(B[A-Z][A-Za-z0-9]*)""").Select(m => m.Groups[1].Value)).ToHashSet();
        named.Where(k => !app.TryGetResource(k, BirkoThemeVariants.Light, out _)).Should().BeEmpty("a key that does not resolve draws nothing");

        var dictionary = System.IO.File.ReadAllText(System.IO.Path.Combine(ControlsDir(), "Ribbon.axaml"));
        var declared = Regex.Matches(dictionary, @"x:Key=""(B[A-Za-z0-9]+)""").Select(m => m.Groups[1].Value).ToList();
        declared.Should().NotBeEmpty();
        declared.Where(k => !named.Contains(k)).Should().BeEmpty("a ribbon size nothing reads is dead");
    }
}
