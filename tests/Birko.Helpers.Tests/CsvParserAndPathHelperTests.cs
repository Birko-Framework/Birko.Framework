using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FluentAssertions;
using Xunit;

namespace Birko.Helpers.Tests;

/// <summary>
/// CR-M196: the two highest-risk untested public helpers — CsvParser (RFC4180-ish quoting/escaping/CRLF
/// state machine) and PathHelper.IsUnderDirectory (sibling-prefix containment).
/// </summary>
public class CsvParserAndPathHelperTests
{
    private static List<IList<string>> ParseCsv(string content, char delimiter = ',', char? enclosure = '"')
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return new CsvParser(stream, delimiter, enclosure).Parse().ToList();
    }

    [Fact]
    public void Parse_SimpleRows()
    {
        var rows = ParseCsv("a,b,c\n1,2,3");
        rows.Should().HaveCount(2);
        rows[0].Should().Equal("a", "b", "c");
        rows[1].Should().Equal("1", "2", "3");
    }

    [Fact]
    public void Parse_QuotedFieldWithDelimiterAndNewline()
    {
        var rows = ParseCsv("\"a,b\",\"line1\nline2\"\nx,y");
        rows[0].Should().Equal("a,b", "line1\nline2");
        rows[1].Should().Equal("x", "y");
    }

    [Fact]
    public void Parse_EscapedQuoteInsideQuotedField()
    {
        // RFC4180: a doubled quote inside a quoted field is a literal quote.
        var rows = ParseCsv("\"she said \"\"hi\"\"\",next");
        rows[0][0].Should().Be("she said \"hi\"");
        rows[0][1].Should().Be("next");
    }

    [Fact]
    public void Parse_CrLfLineEndings()
    {
        var rows = ParseCsv("a,b\r\nc,d\r\n");
        rows.Should().HaveCount(2);
        rows[0].Should().Equal("a", "b");
        rows[1].Should().Equal("c", "d");
    }

    [Fact]
    public void Parse_TrailingRowWithoutNewline()
    {
        var rows = ParseCsv("a,b\nc,d");
        rows.Should().HaveCount(2);
        rows[1].Should().Equal("c", "d");
    }

    [Fact]
    public void Parse_CustomDelimiter()
    {
        var rows = ParseCsv("a;b;c", delimiter: ';');
        rows[0].Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Parse_TrailingRowEndingInBareCarriageReturn_TrimsIt()
    {
        // CR-L271: the last field (no trailing newline) must get the same TrimEnd('\r') the in-loop
        // newline branches apply, so a bare trailing '\r' isn't emitted as a stray carriage return.
        var rows = ParseCsv("a,b\r");

        rows.Should().HaveCount(1);
        rows[0].Should().Equal("a", "b");
    }

    [Fact]
    public void Parse_CancelledToken_ThrowsOperationCanceled()
    {
        // CR-L271: the read loop observes the token so a parse over a slow stream can be cancelled.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b\nc,d\ne,f"));
        var parser = new CsvParser(stream);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => parser.Parse(cts.Token).ToList();

        act.Should().Throw<OperationCanceledException>();
    }

    // ── PathHelper.IsUnderDirectory ─────────────────────────

    /// <summary>
    /// The same five containment cases, built with THIS platform's root and separator.
    /// </summary>
    /// <remarks>
    /// TASK-459: these were <c>[InlineData(@"C:\base\sub\file.txt", @"C:\base", true)]</c> and passed
    /// for years because they had only ever run on Windows. <c>IsUnderDirectory</c> compares against
    /// <see cref="Path.DirectorySeparatorChar"/>, which is <c>/</c> on Unix — so on Linux
    /// <c>C:\base\sub\file.txt</c> is a single filename that happens to contain backslashes, is not
    /// under <c>C:\base</c>, and the assertion failed. The PRODUCT is correct and platform-aware;
    /// the data was not. Building the paths here keeps every case and states the rule on either
    /// platform.
    /// </remarks>
    public static TheoryData<string, string, bool> ContainmentCases()
    {
        var root = OperatingSystem.IsWindows() ? @"C:\" : "/";
        var s = Path.DirectorySeparatorChar;
        return new TheoryData<string, string, bool>
        {
            { $"{root}base{s}sub{s}file.txt",   $"{root}base", true },
            { $"{root}base",                    $"{root}base", true },  // identical path
            { $"{root}base{s}",                 $"{root}base", true },  // trailing separator
            { $"{root}base-sibling{s}file.txt", $"{root}base", false }, // sibling-prefix must NOT match
            { $"{root}other{s}file.txt",        $"{root}base", false },
        };
    }

    [Theory]
    [MemberData(nameof(ContainmentCases))]
    public void IsUnderDirectory_HandlesContainmentAndSiblingPrefix(string fullPath, string dir, bool expected)
    {
        PathHelper.IsUnderDirectory(fullPath, dir).Should().Be(expected);
    }
}
