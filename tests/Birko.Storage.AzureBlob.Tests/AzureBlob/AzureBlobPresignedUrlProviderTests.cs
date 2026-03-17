using FluentAssertions;
using Xunit;

namespace Birko.Storage.AzureBlob.Tests.AzureBlob;

public class AzureBlobPresignedUrlProviderTests
{
    // A valid Base64-encoded 32-byte key for testing
    private const string TestAccountKey = "dGVzdGtleXRlc3RrZXl0ZXN0a2V5dGVzdGtleTE=";
    private const string TestAccountName = "testaccount";
    private const string TestAccountUri = "https://testaccount.blob.core.windows.net";
    private const string TestContainer = "test-container";

    [Fact]
    public void GenerateSasUri_ReadPermission_ContainsExpectedQueryParams()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1);

        var uri = AzureBlobPresignedUrlProvider.GenerateSasUri(
            TestAccountUri, TestContainer, "docs/file.txt",
            TestAccountName, TestAccountKey,
            "r", expiry);

        uri.Should().NotBeNull();
        var query = uri.Query;
        query.Should().Contain("sv=");
        query.Should().Contain("sp=r");
        query.Should().Contain("se=");
        query.Should().Contain("sr=b");
        query.Should().Contain("spr=https");
        query.Should().Contain("sig=");
    }

    [Fact]
    public void GenerateSasUri_WritePermission_ContainsWritePermission()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1);

        var uri = AzureBlobPresignedUrlProvider.GenerateSasUri(
            TestAccountUri, TestContainer, "docs/file.txt",
            TestAccountName, TestAccountKey,
            "w", expiry);

        uri.Query.Should().Contain("sp=w");
    }

    [Fact]
    public void GenerateSasUri_CorrectBasePath()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1);

        var uri = AzureBlobPresignedUrlProvider.GenerateSasUri(
            TestAccountUri, TestContainer, "products/photo.jpg",
            TestAccountName, TestAccountKey,
            "r", expiry);

        uri.AbsolutePath.Should().Be("/test-container/products/photo.jpg");
    }

    [Fact]
    public void GenerateSasUri_TrailingSlashOnAccountUri_NoDuplicateSlash()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1);

        var uri = AzureBlobPresignedUrlProvider.GenerateSasUri(
            TestAccountUri + "/", TestContainer, "file.txt",
            TestAccountName, TestAccountKey,
            "r", expiry);

        uri.AbsolutePath.Should().Be("/test-container/file.txt");
    }

    [Fact]
    public void GenerateSasUri_WithContentDisposition_IncludesRscd()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1);

        var uri = AzureBlobPresignedUrlProvider.GenerateSasUri(
            TestAccountUri, TestContainer, "file.txt",
            TestAccountName, TestAccountKey,
            "r", expiry,
            contentDisposition: "attachment; filename=\"report.pdf\"");

        uri.Query.Should().Contain("rscd=");
    }

    [Fact]
    public void GenerateSasUri_WithContentType_IncludesRsct()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1);

        var uri = AzureBlobPresignedUrlProvider.GenerateSasUri(
            TestAccountUri, TestContainer, "file.txt",
            TestAccountName, TestAccountKey,
            "r", expiry,
            contentType: "application/pdf");

        uri.Query.Should().Contain("rsct=");
    }

    [Fact]
    public void GenerateSasUri_DifferentExpiry_ProducesDifferentSignature()
    {
        var uri1 = AzureBlobPresignedUrlProvider.GenerateSasUri(
            TestAccountUri, TestContainer, "file.txt",
            TestAccountName, TestAccountKey,
            "r", DateTimeOffset.UtcNow.AddHours(1));

        var uri2 = AzureBlobPresignedUrlProvider.GenerateSasUri(
            TestAccountUri, TestContainer, "file.txt",
            TestAccountName, TestAccountKey,
            "r", DateTimeOffset.UtcNow.AddHours(2));

        // Different expiry should produce different signatures
        uri1.Query.Should().NotBe(uri2.Query);
    }

    [Fact]
    public void GenerateSasUri_DifferentPermissions_ProducesDifferentSignature()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1);

        var readUri = AzureBlobPresignedUrlProvider.GenerateSasUri(
            TestAccountUri, TestContainer, "file.txt",
            TestAccountName, TestAccountKey,
            "r", expiry);

        var writeUri = AzureBlobPresignedUrlProvider.GenerateSasUri(
            TestAccountUri, TestContainer, "file.txt",
            TestAccountName, TestAccountKey,
            "w", expiry);

        readUri.Query.Should().NotBe(writeUri.Query);
    }

    [Fact]
    public void GenerateSasUri_UsesHttpsScheme()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1);

        var uri = AzureBlobPresignedUrlProvider.GenerateSasUri(
            TestAccountUri, TestContainer, "file.txt",
            TestAccountName, TestAccountKey,
            "r", expiry);

        uri.Scheme.Should().Be("https");
    }
}
