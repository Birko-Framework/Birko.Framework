using Birko.DesignTokens;
using FluentAssertions;
using Xunit;

namespace Birko.DesignTokens.Tests;

/// <summary>
/// The AXAML counterpart to <see cref="CssParityTests"/>: every generated dictionary on disk must
/// match what tokens.json regenerates. Comparison is line-ending independent (the repo is
/// canonically LF; a working tree may present CRLF under autocrlf), so both sides normalize to LF.
///
/// This exists because the CSS side had a test gate and the AXAML side had only the `verify` CLI
/// verb — which nothing runs automatically. A stale generated tree is therefore invisible until
/// someone regenerates and silently loses the hand-edits, which is exactly what happened to the CSS
/// (see Birko.DesignTokens/CLAUDE.md § "verify covers BOTH targets"). Six generated dictionaries
/// with no suite-level gate would have been the same trap.
/// </summary>
public class AxamlParityTests
{
    private static readonly Paths P = Paths.Resolve(Array.Empty<string>());
    private static readonly TokenSet Tokens = TokenSet.Load(P.TokensJson);
    private static readonly Dictionary<string, string> Generated = AxamlEmitter.Generate(Tokens);

    public static IEnumerable<object[]> Files => Generated.Keys.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(Files))]
    public void Committed_axaml_matches_what_tokens_json_regenerates(string file)
    {
        string path = Path.Combine(P.XamlThemes, file);
        File.Exists(path).Should().BeTrue($"{file} must be generated into {P.XamlThemes}");

        string committed = CssIo.Normalize(CssIo.Read(path));

        CssIo.Normalize(Generated[file]).Should().Be(committed,
            $"{file} is generated — regenerate with the 'generate' command instead of hand-editing it");
    }

    [Fact]
    public void No_stray_dictionaries_are_left_behind_in_the_themes_folder()
    {
        // A renamed/removed theme file must not linger: it would still be merged by anything that
        // references it, serving tokens no longer in the source.
        var onDisk = Directory.EnumerateFiles(P.XamlThemes, "*.axaml")
            .Select(Path.GetFileName)
            .ToList();

        onDisk.Should().BeEquivalentTo(Generated.Keys,
            "the Themes folder must contain exactly the generated set");
    }
}
