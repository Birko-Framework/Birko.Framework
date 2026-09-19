using AuditCommon;
using FluentAssertions;
using Xunit;

namespace AuditCommonTests;

/// <summary>
/// Every test here pins a defect that shipped. TASK-476 ported four root scripts off PowerShell
/// because each carried Windows path assumptions, and three of them failed by silently answering the
/// WRONG QUESTION rather than by erroring — the shape that makes a green run meaningless.
///
/// These are deliberately separator-explicit rather than built with Path.Combine, because the bugs
/// were about what happens when a separator is NOT the local one. A test that constructs its input
/// with Path.Combine cannot see the defect it is supposed to guard.
/// </summary>
public class PathsTests
{
    // ---- FromMsBuild ------------------------------------------------------------------------
    // The killer: consumer imports are written `$(BirkoSrc)\Birko.Helpers\Birko.Helpers.projitems`.
    // That is the MSBuild file format and is backslash-separated on EVERY platform, across all 173
    // projitems in this tree. On Linux the substituted path could not be opened, so the transitive
    // import graph came back empty and audit-consumer-versions reported every consumer as importing
    // nothing — which its own header calls "a defect in this script, not a clean result".

    [Fact]
    public void FromMsBuild_converts_a_backslash_path_to_this_platforms_separator()
    {
        var result = Paths.FromMsBuild(@"root\Birko.Helpers\Birko.Helpers.projitems");

        result.Should().Be(Path.Combine("root", "Birko.Helpers", "Birko.Helpers.projitems"));
        result.Should().NotContain("\\" == Path.DirectorySeparatorChar.ToString() ? "/" : "\\");
    }

    [Fact]
    public void FromMsBuild_also_normalises_forward_slashes()
    {
        // A hand-edited projitems may use forward slashes; MSBuild accepts both, so this must too.
        Paths.FromMsBuild("root/Birko.Helpers/x.projitems")
             .Should().Be(Path.Combine("root", "Birko.Helpers", "x.projitems"));
    }

    [Fact]
    public void FromMsBuild_normalises_a_mixed_separator_path()
    {
        Paths.FromMsBuild(@"root/Birko.Helpers\x.projitems")
             .Should().Be(Path.Combine("root", "Birko.Helpers", "x.projitems"));
    }

    [Fact]
    public void FromMsBuild_leaves_a_path_with_no_separators_alone()
    {
        Paths.FromMsBuild("x.projitems").Should().Be("x.projitems");
    }

    // ---- IsUnderBinObj ----------------------------------------------------------------------
    // The originals filtered build output with `-notmatch '\\(bin|obj)\\'`, which tests for
    // BACKSLASH-delimited segments. On Linux that never matched, so audit-declarations admitted
    // generated obj/**/*.cs into its `using` scan — inventing usings no hand-written source has.

    [Theory]
    [InlineData(@"C:\src\proj\obj\Debug\g.cs")]
    [InlineData(@"C:\src\proj\bin\Debug\x.dll")]
    [InlineData("/home/ci/src/proj/obj/Debug/g.cs")]
    [InlineData("/home/ci/src/proj/bin/Debug/x.dll")]
    [InlineData("proj/obj/g.cs")]
    [InlineData(@"proj\obj\g.cs")]
    public void IsUnderBinObj_matches_regardless_of_which_separator_is_used(string path) =>
        Paths.IsUnderBinObj(path).Should().BeTrue();

    [Theory]
    [InlineData("/home/ci/src/proj/Stores/Thing.cs")]
    [InlineData(@"C:\src\proj\Stores\Thing.cs")]
    public void IsUnderBinObj_is_false_for_an_ordinary_source_path(string path) =>
        Paths.IsUnderBinObj(path).Should().BeFalse();

    [Theory]
    [InlineData("/src/binding/Thing.cs")]      // starts with "bin"
    [InlineData("/src/objects/Thing.cs")]      // starts with "obj"
    [InlineData("/src/Robin/Thing.cs")]        // ends with "bin"
    [InlineData("/src/proj/bin.cs")]           // a FILE called bin.cs, not a directory
    public void IsUnderBinObj_compares_whole_segments_not_substrings(string path) =>
        Paths.IsUnderBinObj(path).Should().BeFalse();

    [Fact]
    public void IsUnderBinObj_case_follows_the_platforms_filesystem()
    {
        // Windows would open OBJ\ and obj\ as the same directory; Linux would not, and treating
        // them alike there would silently drop a real source directory named OBJ.
        Paths.IsUnderBinObj("/src/OBJ/g.cs").Should().Be(OperatingSystem.IsWindows());
    }

    // ---- IsUnderAny -------------------------------------------------------------------------

    [Theory]
    [InlineData("/app/node_modules/pkg/a.csproj")]
    [InlineData(@"C:\app\node_modules\pkg\a.csproj")]
    public void IsUnderAny_finds_a_named_directory_under_either_separator(string path) =>
        Paths.IsUnderAny(path, "node_modules").Should().BeTrue();

    [Fact]
    public void IsUnderAny_is_false_when_no_name_matches() =>
        Paths.IsUnderAny("/app/src/a.csproj", "node_modules", "bin").Should().BeFalse();

    // ---- IdentityKey ------------------------------------------------------------------------
    // The PowerShell original lower-cased every full path before putting it in its "already seen"
    // set. On Linux, where paths are case-sensitive, two genuinely different files collapse into one
    // entry and the second is skipped without a word.

    [Fact]
    public void IdentityKey_folds_case_only_where_the_filesystem_does()
    {
        var upper = Paths.IdentityKey("/src/Thing.cs");
        var lower = Paths.IdentityKey("/src/thing.cs");

        if (OperatingSystem.IsWindows()) upper.Should().Be(lower);
        else upper.Should().NotBe(lower);
    }

    // ---- EnumerateFiles ---------------------------------------------------------------------

    [Fact]
    public void EnumerateFiles_skips_build_output_but_finds_real_sources()
    {
        var root = Directory.CreateTempSubdirectory("auditcommon").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            Directory.CreateDirectory(Path.Combine(root, "obj", "Debug"));
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            File.WriteAllText(Path.Combine(root, "src", "Real.cs"), "");
            File.WriteAllText(Path.Combine(root, "obj", "Debug", "Generated.cs"), "");
            File.WriteAllText(Path.Combine(root, "bin", "Copied.cs"), "");

            var found = Paths.EnumerateFiles(root, "*.cs").Select(Path.GetFileName).ToArray();

            found.Should().BeEquivalentTo(["Real.cs"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_returns_nothing_for_a_directory_that_does_not_exist() =>
        Paths.EnumerateFiles(Path.Combine(Path.GetTempPath(), "auditcommon-absent-" + Guid.NewGuid()), "*.cs")
             .Should().BeEmpty();
}
