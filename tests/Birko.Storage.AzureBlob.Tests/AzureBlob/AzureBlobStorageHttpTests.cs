using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Storage.AzureBlob.Tests.AzureBlob;

/// <summary>
/// CR-L375: HTTP-driven behavior (NotFound mapping, header/XML parsing, exists, list, overwrite gating,
/// token caching) was previously untested — every test stopped before the first HTTP call. These drive a
/// stub HttpMessageHandler against canned responses. Also exercises the CR-L371 response-disposal paths.
/// </summary>
public class AzureBlobStorageHttpTests
{
    private readonly AzureBlobSettings _settings = new(
        "https://testaccount.blob.core.windows.net",
        "test-container",
        "tenant-id",
        "client-id",
        "client-secret");

    private readonly IDateTimeProvider _clock = new SystemDateTimeProvider();

    /// <summary>Answers the OAuth token endpoint automatically and delegates blob requests to a responder.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public int TokenRequests { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.RequestUri!.Host == "login.microsoftonline.com")
            {
                TokenRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"test-token\",\"expires_in\":3600}")
                });
            }
            return Task.FromResult(_responder(request));
        }
    }

    private (AzureBlobStorage storage, StubHandler handler, HttpClient client) Create(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var client = new HttpClient(handler);
        var storage = new AzureBlobStorage(_settings, _clock, client);
        return (storage, handler, client);
    }

    [Fact]
    public async Task DownloadAsync_NotFound_MapsToNotFoundResult()
    {
        var (storage, _, client) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using (storage) using (client)
        {
            var result = await storage.DownloadAsync("missing.txt");
            result.Found.Should().BeFalse();
            result.Value.Should().BeNull();
        }
    }

    [Fact]
    public async Task DownloadAsync_Success_ReturnsStreamAndSendsBearerToken()
    {
        var payload = Encoding.UTF8.GetBytes("hello blob");
        var (storage, handler, client) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });
        using (storage) using (client)
        {
            var result = await storage.DownloadAsync("file.txt");

            result.Found.Should().BeTrue();
            using var reader = new StreamReader(result.Value!);
            (await reader.ReadToEndAsync()).Should().Be("hello blob");

            var blobRequest = handler.Requests.Single(r => r.RequestUri!.Host != "login.microsoftonline.com");
            blobRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
            blobRequest.Headers.Authorization.Parameter.Should().Be("test-token");
        }
    }

    [Fact]
    public async Task GetReferenceAsync_ParsesHeadersIntoReference()
    {
        var (storage, _, client) = Create(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[42])
            };
            resp.Content.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
            resp.Content.Headers.LastModified = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
            resp.Headers.ETag = new EntityTagHeaderValue("\"abc123\"");
            resp.Headers.Add("x-ms-creation-time", "Wed, 01 Jan 2020 00:00:00 GMT");
            resp.Headers.Add("x-ms-meta-owner", "alice");
            return resp;
        });
        using (storage) using (client)
        {
            var result = await storage.GetReferenceAsync("doc.md");

            result.Found.Should().BeTrue();
            var reference = result.Value!;
            reference.ContentType.Should().Be("text/markdown");
            reference.Size.Should().Be(42);
            reference.ETag.Should().Be("abc123");
            reference.LastModifiedAt.Should().Be(new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero));
            reference.CreatedAt.Should().Be(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
            reference.Metadata.Should().ContainKey("owner").WhoseValue.Should().Be("alice");
        }
    }

    [Fact]
    public async Task GetReferenceAsync_NotFound_MapsToNotFoundResult()
    {
        var (storage, _, client) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using (storage) using (client)
        {
            var result = await storage.GetReferenceAsync("missing.txt");
            result.Found.Should().BeFalse();
        }
    }

    [Fact]
    public async Task ListAsync_ParsesBlobXml()
    {
        const string xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <EnumerationResults>
          <Blobs>
            <Blob>
              <Name>alpha.txt</Name>
              <Properties>
                <Content-Length>10</Content-Length>
                <Content-Type>text/plain</Content-Type>
                <Last-Modified>Wed, 01 Jan 2020 00:00:00 GMT</Last-Modified>
                <Etag>"tag-a"</Etag>
              </Properties>
              <Metadata><owner>bob</owner></Metadata>
            </Blob>
            <Blob>
              <Name>beta.bin</Name>
              <Properties>
                <Content-Length>20</Content-Length>
                <Content-Type>application/octet-stream</Content-Type>
              </Properties>
            </Blob>
          </Blobs>
        </EnumerationResults>
        """;
        var (storage, _, client) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(xml)
        });
        using (storage) using (client)
        {
            var list = await storage.ListAsync();

            list.Should().HaveCount(2);
            var alpha = list.Single(f => f.Path == "alpha.txt");
            alpha.Size.Should().Be(10);
            alpha.ContentType.Should().Be("text/plain");
            alpha.ETag.Should().Be("tag-a");
            alpha.Metadata.Should().ContainKey("owner").WhoseValue.Should().Be("bob");

            var beta = list.Single(f => f.Path == "beta.bin");
            beta.Size.Should().Be(20);
        }
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrueOn200_FalseOn404()
    {
        var (storageT, _, clientT) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using (storageT) using (clientT)
        {
            (await storageT.ExistsAsync("there.txt")).Should().BeTrue();
        }

        var (storageF, _, clientF) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using (storageF) using (clientF)
        {
            (await storageF.ExistsAsync("gone.txt")).Should().BeFalse();
        }
    }

    [Fact]
    public async Task UploadAsync_OverwriteFalse_ExistingBlob_ThrowsFileAlreadyExists()
    {
        // The HEAD existence probe returns 200 → Upload must abort before the PUT.
        var (storage, handler, client) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using (storage) using (client)
        {
            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            var options = new StorageOptions { OverwriteExisting = false };

            var act = () => storage.UploadAsync("dup.txt", stream, "text/plain", options);

            await act.Should().ThrowAsync<FileAlreadyExistsException>();
            // Only the token request + the HEAD probe — no PUT was issued.
            handler.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);
        }
    }

    [Fact]
    public async Task AccessToken_IsCachedAcrossCalls()
    {
        var (storage, handler, client) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using (storage) using (client)
        {
            await storage.ExistsAsync("a.txt");
            await storage.ExistsAsync("b.txt");

            handler.TokenRequests.Should().Be(1);
        }
    }
}
