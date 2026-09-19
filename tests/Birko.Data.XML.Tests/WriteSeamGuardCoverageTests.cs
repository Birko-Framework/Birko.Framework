using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Birko.Data.XML.Tests;

/// <summary>
/// Every write seam in the JSON and XML file stores calls <c>EnsureWritable()</c> first.
/// </summary>
/// <remarks>
/// <para>
/// The behavioural tests prove today's stores refuse a write they cannot persist. This proves the
/// <em>next</em> one will: a store that adds a <c>CreateCore</c>/<c>UpdateCore</c>/<c>DeleteCore</c>
/// override without the call silently reopens TASK-464, and nothing else in the suite notices —
/// the defect is an absence, and an absence has no failing assertion of its own.
/// </para>
/// <para>
/// Why a source scan rather than reflection: the rule is about a method's <b>body</b>, which
/// reflection cannot see. § Conventions records the shape this guards — *a funnel with four
/// overrides is not a funnel* — and here the funnel really does have overrides: the separate- and
/// batch-file stores bypass the base's persistence entirely and write per-entity files, which is
/// exactly why the guard cannot live only on the base.
/// </para>
/// <para>
/// It scans both projects from one place deliberately. The rule is one rule; two copies of the scan
/// would be two things to keep in step, and this is the file that already owns the XML half.
/// </para>
/// </remarks>
public class WriteSeamGuardCoverageTests
{
    /// <summary>A write seam: the overrides that mutate stored state.</summary>
    private static readonly Regex WriteSeam = new(
        @"protected override (?:async )?[^\n(]*\b(?<name>(?:Create|Update|Delete)Core(?:Async)?)\s*\([^;{]*?\)\s*\r?\n(?<indent>\s*)\{\r?\n(?<first>[^\n]*)",
        RegexOptions.Compiled);

    private static IEnumerable<string> StoreSources()
    {
        var root = FrameworkRoot();
        foreach (var project in new[] { "Birko.Data.JSON", "Birko.Data.XML" })
        {
            var dir = Path.Combine(root, project, "Stores");
            Directory.Exists(dir).Should().BeTrue("{0} is where these stores live", dir);
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
            {
                yield return file;
            }
        }
    }

    [Fact]
    public void Every_write_seam_calls_EnsureWritable_first()
    {
        var offenders = new List<string>();

        foreach (var file in StoreSources())
        {
            var text = File.ReadAllText(file);
            foreach (Match m in WriteSeam.Matches(text))
            {
                if (!m.Groups["first"].Value.Contains("EnsureWritable()", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{m.Groups["name"].Value}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "a write seam that does not call EnsureWritable() first accepts a write it cannot persist, "
            + "and reports success — measured on the shipped code as Create returning a Guid, Count "
            + "reporting 1, and nothing reaching disk");
    }

    [Fact]
    public void The_scan_actually_finds_seams()
    {
        // Without this, a regex that silently stopped matching would make the test above vacuous —
        // it would report zero offenders because it found nothing at all. The count is a floor, not
        // an exact figure, so adding a store does not fail it.
        var seams = StoreSources().Sum(f => WriteSeam.Matches(File.ReadAllText(f)).Count);

        seams.Should().BeGreaterThanOrEqualTo(24,
            "the four abstract bases carry six write seams each, before the XML separate- and "
            + "batch-file stores add their own overrides");
    }

    private static string FrameworkRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "")
    {
        // .../Framework/tests/Birko.Data.XML.Tests/<this file>
        var dir = Path.GetDirectoryName(path)!;
        return Path.GetFullPath(Path.Combine(dir, "..", ".."));
    }
}
