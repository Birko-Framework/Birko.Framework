using Birko.Storage.AzureBlob;
using FluentAssertions;
using Xunit;

namespace Birko.Storage.AzureBlob.Tests.AzureBlob;

/// <summary>
/// CR-M248: SAS/REST URLs appended the raw blobPath, so keys with spaces or URI-reserved characters
/// produced a malformed/mis-routed URL (and could mismatch the signature). Each path segment must be
/// percent-encoded while the '/' separators are preserved.
/// </summary>
public class BlobPathEncodingTests
{
    [Theory]
    [InlineData("simple.txt", "simple.txt")]
    [InlineData("folder/sub/file.txt", "folder/sub/file.txt")]        // slashes preserved
    [InlineData("my folder/a file.txt", "my%20folder/a%20file.txt")]  // spaces encoded per segment
    [InlineData("a#b/c?d.txt", "a%23b/c%3Fd.txt")]                    // reserved chars encoded
    [InlineData("100%/x.txt", "100%25/x.txt")]
    public void EncodeBlobPath_EncodesSegments_PreservesSlashes(string input, string expected)
    {
        AzureBlobPresignedUrlProvider.EncodeBlobPath(input).Should().Be(expected);
    }

    [Fact]
    public void EncodeBlobPath_EmptyOrNull_ReturnedAsIs()
    {
        AzureBlobPresignedUrlProvider.EncodeBlobPath("").Should().Be("");
        AzureBlobPresignedUrlProvider.EncodeBlobPath(null!).Should().BeNull();
    }
}
