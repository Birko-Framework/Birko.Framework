using FluentAssertions;
using Xunit;

namespace Birko.Helpers.Tests;

/// <summary>
/// CR-M195: SanitizePath stripped traversal tokens in a single non-recursive pass, so nested/overlapping
/// sequences re-formed a token after one removal (e.g. "....//" → "../"). It now loops to a fixpoint, so
/// no "../" / "..\" token survives regardless of nesting depth.
/// </summary>
public class SanitizePathTests
{
    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("....//etc")]
    [InlineData("....\\\\windows")]
    [InlineData("..././..././data")]
    [InlineData("....//....//secret")]
    [InlineData("..\\..\\windows")]
    public void SanitizePath_LeavesNoTraversalToken(string input)
    {
        var result = PathValidator.SanitizePath(input);
        result.Should().NotContain("../");
        result.Should().NotContain("..\\");
    }

    [Fact]
    public void SanitizePath_PreservesBenignPath()
    {
        PathValidator.SanitizePath("normal/path.txt").Should().Be("normal/path.txt");
    }

    [Fact]
    public void SanitizePath_StripsLeadingSeparatorsAndDriveLetter()
    {
        PathValidator.SanitizePath("/abs/path").Should().Be("abs/path");
        PathValidator.SanitizePath("C:\\windows").Should().NotContain(":");
    }

    [Fact]
    public void SanitizePath_RemovesNullCharacters()
    {
        PathValidator.SanitizePath("a\0b").Should().Be("ab");
    }
}
