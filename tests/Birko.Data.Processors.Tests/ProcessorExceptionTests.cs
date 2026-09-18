using System;
using Birko.Data.Processors;
using FluentAssertions;

namespace Birko.Data.Processors.Tests;

/// <summary>
/// CR-L161: ProcessorParseException must not fabricate a synthetic inner Exception when none is
/// supplied — that polluted exception chains with a misleading "caused by" carrying a duplicate message.
/// </summary>
public class ProcessorExceptionTests
{
    [Fact]
    public void ParseException_WithoutInner_HasNullInnerException()
    {
        var ex = new ProcessorParseException("bad row", element: "col0");

        ex.InnerException.Should().BeNull();
        ex.Element.Should().Be("col0");
        ex.Message.Should().Be("bad row");
    }

    [Fact]
    public void ParseException_WithInner_PreservesIt()
    {
        var inner = new FormatException("nope");
        var ex = new ProcessorParseException("bad row", element: "col0", innerException: inner);

        ex.InnerException.Should().BeSameAs(inner);
    }
}
