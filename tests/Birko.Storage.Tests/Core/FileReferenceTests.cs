using FluentAssertions;
using Xunit;

namespace Birko.Storage.Tests.Core;

public class FileReferenceTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var reference = new FileReference();

        reference.Path.Should().BeEmpty();
        reference.FileName.Should().BeEmpty();
        reference.ContentType.Should().BeEmpty();
        reference.Size.Should().Be(0);
        reference.ETag.Should().BeNull();
        reference.LastModifiedAt.Should().BeNull();
        reference.Metadata.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var now = DateTimeOffset.UtcNow;
        var reference = new FileReference
        {
            Path = "images/photo.jpg",
            FileName = "photo.jpg",
            ContentType = "image/jpeg",
            Size = 1024,
            CreatedAt = now,
            LastModifiedAt = now,
            ETag = "abc123",
            Metadata = new Dictionary<string, string> { ["author"] = "test" }
        };

        reference.Path.Should().Be("images/photo.jpg");
        reference.FileName.Should().Be("photo.jpg");
        reference.ContentType.Should().Be("image/jpeg");
        reference.Size.Should().Be(1024);
        reference.CreatedAt.Should().Be(now);
        reference.LastModifiedAt.Should().Be(now);
        reference.ETag.Should().Be("abc123");
        reference.Metadata.Should().ContainKey("author").WhoseValue.Should().Be("test");
    }
}
