using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

    // ── PathHelper.IsUnderDirectory ─────────────────────────

    [Theory]
    [InlineData(@"C:\base\sub\file.txt", @"C:\base", true)]
    [InlineData(@"C:\base", @"C:\base", true)]                 // identical path
    [InlineData(@"C:\base\", @"C:\base", true)]                // trailing separator
    [InlineData(@"C:\base-sibling\file.txt", @"C:\base", false)] // sibling-prefix must NOT match
    [InlineData(@"C:\other\file.txt", @"C:\base", false)]
    public void IsUnderDirectory_HandlesContainmentAndSiblingPrefix(string fullPath, string dir, bool expected)
    {
        PathHelper.IsUnderDirectory(fullPath, dir).Should().Be(expected);
    }
}
