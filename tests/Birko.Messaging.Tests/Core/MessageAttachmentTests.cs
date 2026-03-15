using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Birko.Messaging.Tests.Core;

public class MessageAttachmentTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        using var stream = new MemoryStream();
        var attachment = new MessageAttachment("doc.pdf", "application/pdf", stream, true, "cid-1");

        attachment.FileName.Should().Be("doc.pdf");
        attachment.ContentType.Should().Be("application/pdf");
        attachment.Content.Should().BeSameAs(stream);
        attachment.IsInline.Should().BeTrue();
        attachment.ContentId.Should().Be("cid-1");
    }

    [Fact]
    public void Constructor_NullFileName_ThrowsArgumentNullException()
    {
        using var stream = new MemoryStream();
        var act = () => new MessageAttachment(null!, "text/plain", stream);

        act.Should().Throw<ArgumentNullException>().WithParameterName("fileName");
    }

    [Fact]
    public void Constructor_NullContentType_ThrowsArgumentNullException()
    {
        using var stream = new MemoryStream();
        var act = () => new MessageAttachment("file.txt", null!, stream);

        act.Should().Throw<ArgumentNullException>().WithParameterName("contentType");
    }

    [Fact]
    public void Constructor_NullContent_ThrowsArgumentNullException()
    {
        var act = () => new MessageAttachment("file.txt", "text/plain", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("content");
    }

    [Fact]
    public void DefaultIsInline_IsFalse()
    {
        using var stream = new MemoryStream();
        var attachment = new MessageAttachment("file.txt", "text/plain", stream);

        attachment.IsInline.Should().BeFalse();
        attachment.ContentId.Should().BeNull();
    }
}
