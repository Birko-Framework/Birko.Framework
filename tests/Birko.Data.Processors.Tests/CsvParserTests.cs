using System.Text;
using Birko.Helpers;
using FluentAssertions;

namespace Birko.Data.Processors.Tests;

public class CsvParserTests
{
    private static Stream ToStream(string content, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        return new MemoryStream(encoding.GetBytes(content));
    }

    [Fact]
    public void Parse_SimpleRows_ReturnsCorrectColumns()
    {
        using var stream = ToStream("a,b,c\n1,2,3\n");
        var parser = new CsvParser(stream);
        var rows = parser.Parse().ToList();

        rows.Should().HaveCount(2);
        rows[0].Should().BeEquivalentTo(["a", "b", "c"]);
        rows[1].Should().BeEquivalentTo(["1", "2", "3"]);
    }

    [Fact]
    public void Parse_QuotedFields_HandlesQuotes()
    {
        using var stream = ToStream("\"hello, world\",\"foo\"\n");
        var parser = new CsvParser(stream);
        var rows = parser.Parse().ToList();

        rows.Should().HaveCount(1);
        rows[0][0].Should().Be("hello, world");
        rows[0][1].Should().Be("foo");
    }

    [Fact]
    public void Parse_EscapedQuotes_HandlesDoubledQuotes()
    {
        using var stream = ToStream("\"he said \"\"hi\"\"\",normal\n");
        var parser = new CsvParser(stream);
        var rows = parser.Parse().ToList();

        rows.Should().HaveCount(1);
        rows[0][0].Should().Be("he said \"hi\"");
        rows[0][1].Should().Be("normal");
    }

    [Fact]
    public void Parse_CustomDelimiter_UsesSemicolon()
    {
        using var stream = ToStream("a;b;c\n1;2;3\n");
        var parser = new CsvParser(stream, delimiter: ';');
        var rows = parser.Parse().ToList();

        rows.Should().HaveCount(2);
        rows[0].Should().BeEquivalentTo(["a", "b", "c"]);
    }

    [Fact]
    public void Parse_NoTrailingNewline_ReturnsLastRow()
    {
        using var stream = ToStream("a,b\n1,2");
        var parser = new CsvParser(stream);
        var rows = parser.Parse().ToList();

        rows.Should().HaveCount(2);
        rows[1].Should().BeEquivalentTo(["1", "2"]);
    }

    [Fact]
    public void Parse_EmptyStream_ReturnsNoRows()
    {
        using var stream = ToStream("");
        var parser = new CsvParser(stream);
        var rows = parser.Parse().ToList();

        rows.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NoEnclosure_TreatsQuotesAsData()
    {
        using var stream = ToStream("\"a\",b\n");
        var parser = new CsvParser(stream, enclosure: null);
        var rows = parser.Parse().ToList();

        rows.Should().HaveCount(1);
        rows[0][0].Should().Be("\"a\"");
    }

    [Fact]
    public void Parse_MultilineQuotedField_HandlesNewlinesInQuotes()
    {
        using var stream = ToStream("\"line1\nline2\",b\n");
        var parser = new CsvParser(stream);
        var rows = parser.Parse().ToList();

        rows.Should().HaveCount(1);
        rows[0][0].Should().Be("line1\nline2");
        rows[0][1].Should().Be("b");
    }

    [Fact]
    public void Parse_TracksLineNumber()
    {
        using var stream = ToStream("a\nb\nc\n");
        var parser = new CsvParser(stream);
        var rows = parser.Parse().ToList();

        rows.Should().HaveCount(3);
        parser.Line.Should().Be(3);
    }
}
