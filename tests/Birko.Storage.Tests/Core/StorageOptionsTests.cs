using FluentAssertions;
using Xunit;

namespace Birko.Storage.Tests.Core;

public class StorageOptionsTests
{
    [Fact]
    public void Default_HasExpectedValues()
    {
        var options = StorageOptions.Default;

        options.MaxFileSize.Should().BeNull();
        options.AllowedContentTypes.Should().BeNull();
        options.OverwriteExisting.Should().BeFalse();
        options.Metadata.Should().BeNull();
        options.ContentDisposition.Should().BeNull();
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var options = new StorageOptions
        {
            MaxFileSize = 1024 * 1024,
            AllowedContentTypes = new[] { "image/jpeg", "image/png" },
            OverwriteExisting = true,
            Metadata = new Dictionary<string, string> { ["key"] = "value" },
            ContentDisposition = "inline"
        };

        options.MaxFileSize.Should().Be(1024 * 1024);
        options.AllowedContentTypes.Should().HaveCount(2);
        options.OverwriteExisting.Should().BeTrue();
        options.Metadata.Should().ContainKey("key");
        options.ContentDisposition.Should().Be("inline");
    }
}
