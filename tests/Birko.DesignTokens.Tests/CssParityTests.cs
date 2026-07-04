using System.Xml.Linq;
using Birko.DesignTokens;
using FluentAssertions;
using Xunit;

namespace Birko.DesignTokens.Tests;

/// <summary>
/// The STORY-029 acceptance gate: tokens.json must regenerate the hand-authored web CSS
/// byte-for-byte. Comparison is line-ending independent (the repo is canonically LF; a given
/// working tree may present CRLF under autocrlf), so both sides are normalized to LF first.
/// </summary>
public class CssParityTests
{
    private static readonly Paths P = Paths.Resolve(Array.Empty<string>());
    private static readonly TokenSet Tokens = TokenSet.Load(P.TokensJson);

    public static IEnumerable<object[]> Sheets =>
        Tokens.Sheets.Select(s => new object[] { s.File });

    [Theory]
    [MemberData(nameof(Sheets))]
    public void Regenerated_css_is_byte_identical_to_committed_file(string file)
    {
        var sheet = Tokens.Sheets.Single(s => s.File == file);
        string committed = CssIo.Normalize(CssIo.Read(CssPath(file)));

        string regenerated = CssIo.Emit(sheet);

        regenerated.Should().Be(committed,
            $"regenerating {file} from tokens.json must reproduce the hand-authored file exactly");
    }

    [Fact]
    public void All_five_source_sheets_are_present()
    {
        Tokens.Sheets.Select(s => s.Theme)
            .Should().BeEquivalentTo(new[] { "light", "dark", "neon", "finstat", "inverse" });
    }

    [Fact]
    public void Every_sheet_has_a_selector_and_a_non_empty_body()
    {
        foreach (var s in Tokens.Sheets)
        {
            s.Selector.Should().NotBeNullOrWhiteSpace($"{s.File} needs a CSS selector");
            s.Body.Should().NotBeEmpty($"{s.File} should have body lines");
        }
    }

    [Fact]
    public void Token_values_are_single_sourced_within_a_sheet()
    {
        // A var name must not be declared twice in the same sheet (that would be an ambiguous source).
        foreach (var s in Tokens.Sheets)
        {
            var names = s.Body.Where(n => n.IsVar).Select(n => n.Name!).ToList();
            names.Should().OnlyHaveUniqueItems($"{s.File} must declare each token once");
        }
    }

    [Fact]
    public void Extract_then_emit_round_trips_the_live_css()
    {
        // Independent of tokens.json: prove the extractor itself loses nothing on the live files.
        foreach (var (file, theme) in Paths.SheetManifest)
        {
            string css = CssIo.Normalize(CssIo.Read(CssPath(file)));
            var sheet = CssIo.Extract(css, file, theme);
            CssIo.Emit(sheet).Should().Be(css, $"extract/emit must round-trip {file}");
        }
    }

    private static string CssPath(string file) =>
        Path.Combine(P.WebCss, file.Replace('/', Path.DirectorySeparatorChar));
}
