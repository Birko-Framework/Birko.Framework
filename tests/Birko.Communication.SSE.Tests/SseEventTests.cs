using System.Linq;
using Birko.Communication.SSE;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.SSE.Tests;

/// <summary>
/// CR-H035: the SSE library had no tests. This covers the SseEvent wire format (the W3C
/// text/event-stream serialization) and the JSON/comment factories.
/// </summary>
public class SseEventTests
{
    [Fact]
    public void ToString_EmitsIdEventRetryAndData()
    {
        var e = new SseEvent { Id = "42", Event = "update", Retry = 3000, Data = "hello" };

        var text = e.ToString();

        text.Should().Contain("id: 42");
        text.Should().Contain("event: update");
        text.Should().Contain("retry: 3000");
        text.Should().Contain("data: hello");
        text.Should().EndWith("\n"); // blank line terminates the event
    }

    [Fact]
    public void ToString_MultiLineData_PrefixesEachLine()
    {
        var e = new SseEvent { Data = "line1\nline2\r\nline3" };

        var dataLines = e.ToString()
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.StartsWith("data: "))
            .ToList();

        dataLines.Should().HaveCount(3);
        dataLines.Should().Contain("data: line1");
        dataLines.Should().Contain("data: line2"); // trailing \r trimmed
        dataLines.Should().Contain("data: line3");
    }

    [Fact]
    public void ToString_OmitsAbsentFields()
    {
        var text = new SseEvent { Data = "x" }.ToString();

        text.Should().NotContain("id:");
        text.Should().NotContain("event:");
        text.Should().NotContain("retry:");
    }

    [Fact]
    public void FromJson_SerializesData_AndDefaultsId()
    {
        var e = SseEvent.FromJson(new { name = "acme", count = 3 }, @event: "created");

        e.Event.Should().Be("created");
        e.Id.Should().NotBeNullOrEmpty("FromJson defaults the id to a GUID");
        e.Data.Should().Contain("acme").And.Contain("3");
    }

    [Fact]
    public void CreateComment_EmitsRealSseComment_NotADataField()
    {
        // CR-M072: CreateComment used to smuggle ": text" through Data, so ToString emitted
        // `data: : text` (a data field) instead of a real SSE comment line.
        var e = SseEvent.CreateComment("keep-alive");

        e.Comment.Should().Be("keep-alive");
        e.Data.Should().BeNull();

        var rendered = e.ToString();
        rendered.Should().Contain(": keep-alive");
        rendered.Should().NotContain("data:", "a comment must not be rendered as a data field");
    }

    [Fact]
    public void Create_SetsAllFields()
    {
        var e = SseEvent.Create("payload", @event: "evt", id: "7", retry: 500);

        e.Data.Should().Be("payload");
        e.Event.Should().Be("evt");
        e.Id.Should().Be("7");
        e.Retry.Should().Be(500);
    }
}
