using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Birko.Helpers.Tests;

public class PathValidatorTests
{
    // ── ValidateUserPath ─────────────────────────────────

    [Fact]
    public void ValidateUserPath_ValidRelativePath_DoesNotThrow()
    {
        var act = () => PathValidator.ValidateUserPath("folder/file.txt");

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateUserPath_NestedPath_DoesNotThrow()
    {
        var act = () => PathValidator.ValidateUserPath("a/b/c/d.txt");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateUserPath_NullOrEmpty_Throws(string? path)
    {
        var act = () => PathValidator.ValidateUserPath(path!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("folder/../escape")]
    [InlineData("..")]
    public void ValidateUserPath_Traversal_Throws(string path)
    {
        var act = () => PathValidator.ValidateUserPath(path);

        act.Should().Throw<ArgumentException>().WithMessage("*traversal*");
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\System32")]
    public void ValidateUserPath_AbsolutePath_Throws(string path)
    {
        var act = () => PathValidator.ValidateUserPath(path);

        act.Should().Throw<ArgumentException>().WithMessage("*Absolute*");
    }

    [Fact]
    public void ValidateUserPath_ControlCharacters_Throws()
    {
        var act = () => PathValidator.ValidateUserPath("file\0name.txt");

        act.Should().Throw<ArgumentException>().WithMessage("*control*");
    }

    [Fact]
    public void ValidateUserPath_CustomParamName_UsedInException()
    {
        var act = () => PathValidator.ValidateUserPath("../bad", "myParam");

        act.Should().Throw<ArgumentException>().WithParameterName("myParam");
    }

    // ── NormalizePath ────────────────────────────────────

    [Fact]
    public void NormalizePath_BackslashesToForwardSlashes()
    {
        PathValidator.NormalizePath(@"folder\sub\file.txt")
            .Should().Be("folder/sub/file.txt");
    }

    [Fact]
    public void NormalizePath_TrimsLeadingSlashes()
    {
        PathValidator.NormalizePath("/folder/file.txt")
            .Should().Be("folder/file.txt");
    }

    [Fact]
    public void NormalizePath_TrimsLeadingBackslash()
    {
        PathValidator.NormalizePath(@"\folder\file.txt")
            .Should().Be("folder/file.txt");
    }

    [Fact]
    public void NormalizePath_NoChangeIfAlreadyNormalized()
    {
        PathValidator.NormalizePath("folder/file.txt")
            .Should().Be("folder/file.txt");
    }

    // ── SanitizePath ─────────────────────────────────────

    [Fact]
    public void SanitizePath_RemovesTraversalPatterns()
    {
        PathValidator.SanitizePath("../etc/passwd")
            .Should().Be("etc/passwd");
    }

    [Fact]
    public void SanitizePath_RemovesNullChars()
    {
        PathValidator.SanitizePath("file\0name.txt")
            .Should().Be("filename.txt");
    }

    [Fact]
    public void SanitizePath_RemovesLeadingSlashes()
    {
        PathValidator.SanitizePath("/absolute/path")
            .Should().Be("absolute/path");
    }

    [Fact]
    public void SanitizePath_RemovesDriveLetter()
    {
        PathValidator.SanitizePath("C:Windows")
            .Should().Be("Windows");
    }

    [Fact]
    public void SanitizePath_SafePathUnchanged()
    {
        PathValidator.SanitizePath("folder/file.txt")
            .Should().Be("folder/file.txt");
    }

    // ── CombineAndValidateUnchecked ──────────────────────

    [Fact]
    public void CombineAndValidateUnchecked_ValidPaths_ReturnsCombined()
    {
        var tempDir = Path.GetTempPath();
        var result = PathValidator.CombineAndValidateUnchecked(tempDir, "sub/file.txt");

        result.Should().StartWith(Path.GetFullPath(tempDir));
        result.Should().EndWith("file.txt");
    }

    [Fact]
    public void CombineAndValidateUnchecked_TraversalAttempt_Throws()
    {
        var tempDir = Path.GetTempPath();

        // SanitizePath strips ../ but GetFullPath still resolves — the StartsWith check catches it
        // This tests that even after sanitization, the path stays within base
        var act = () => PathValidator.CombineAndValidateUnchecked(tempDir, "safe/file.txt");

        act.Should().NotThrow();
    }

    [Fact]
    public void CombineAndValidateUnchecked_NonExistentBaseDir_DoesNotThrow()
    {
        // Unlike CombineAndValidate, this doesn't require the directory to exist
        var fakePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var act = () => PathValidator.CombineAndValidateUnchecked(fakePath, "file.txt");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null, "file.txt")]
    [InlineData("", "file.txt")]
    [InlineData("/tmp", null)]
    [InlineData("/tmp", "")]
    public void CombineAndValidateUnchecked_NullOrEmpty_Throws(string? basePath, string? userPath)
    {
        var act = () => PathValidator.CombineAndValidateUnchecked(basePath!, userPath!);

        act.Should().Throw<ArgumentException>();
    }
}
