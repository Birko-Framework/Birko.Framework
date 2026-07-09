using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.NFC.Models;
using Birko.Communication.NFC.Transports;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.NFC.Tests;

/// <summary>
/// Covers <see cref="HttpNfcTransport"/> against a mocked <see cref="HttpMessageHandler"/> (CR-M057):
/// the status/tag/apdu endpoints, 204 NoContent handling, ownsClient disposal, and the CR-M056
/// regression that a background poll fault surfaces via <c>PollingError</c> instead of dying silently.
/// Everything runs offline — no request ever leaves the stub handler.
/// </summary>
public class HttpNfcTransportTests
{
    private const string BaseUrl = "http://reader.local";

    /// <summary>Routes responses by the request's absolute path; records requests + bodies.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;
        public List<string> Paths { get; } = new();
        public List<string> Bodies { get; } = new();

        public StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder)
            => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content != null
                ? await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
                : string.Empty;
            Paths.Add(request.RequestUri!.AbsolutePath);
            Bodies.Add(body);
            return _responder(request, body);
        }
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpNfcTransport Build(Func<HttpRequestMessage, string, HttpResponseMessage> responder, out StubHandler handler)
    {
        handler = new StubHandler(responder);
        return new HttpNfcTransport(BaseUrl, new HttpClient(handler));
    }

    // ── Construction ──

    [Fact]
    public void Name_IsHttp()
    {
        using var transport = new HttpNfcTransport(BaseUrl);
        transport.Name.Should().Be("HTTP");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Ctor_EmptyBaseUrl_Throws(string? baseUrl)
    {
        var act = () => new HttpNfcTransport(baseUrl!);
        act.Should().Throw<ArgumentException>();
    }

    // ── Status endpoint / ConnectAsync ──

    [Fact]
    public async Task ConnectAsync_StatusOk_SetsIsConnected()
    {
        var transport = Build((req, _) => Ok("{}"), out var handler);
        using (transport)
        {
            await transport.ConnectAsync();

            transport.IsConnected.Should().BeTrue();
            handler.Paths.Should().Contain("/api/nfc/status");
        }
    }

    [Fact]
    public async Task ConnectAsync_StatusError_ThrowsAndStaysDisconnected()
    {
        using var transport = Build((req, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError), out _);

        var act = async () => await transport.ConnectAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
        transport.IsConnected.Should().BeFalse();
    }

    // ── Tag endpoint / ReadTagAsync ──

    [Fact]
    public async Task ReadTagAsync_TagJson_ReturnsTag()
    {
        using var transport = Build((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/status")) return Ok("{}");
            return Ok("{\"uid\":\"04A1B2C3\",\"tagType\":10}");
        }, out _);
        await transport.ConnectAsync();

        var tag = await transport.ReadTagAsync(1000);

        tag.Should().NotBeNull();
        tag!.Uid.Should().Be("04A1B2C3");
        tag.TagType.Should().Be(NfcTagType.MifareClassic); // 10
    }

    [Fact]
    public async Task ReadTagAsync_NoContent_ReturnsNull()
    {
        using var transport = Build((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/status")) return Ok("{}");
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }, out _);
        await transport.ConnectAsync();

        var tag = await transport.ReadTagAsync(1000);

        tag.Should().BeNull("204 NoContent means no tag on the reader");
    }

    [Fact]
    public async Task ReadTagAsync_NotConnected_Throws()
    {
        using var transport = Build((req, _) => Ok("{}"), out _);
        var act = async () => await transport.ReadTagAsync(1000);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── APDU endpoint / TransceiveAsync ──

    [Fact]
    public async Task TransceiveAsync_RoundTrips_HexApdu()
    {
        using var transport = Build((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/status")) return Ok("{}");
            return Ok("\"9000\""); // reader echoes back a quoted hex string
        }, out var handler);
        await transport.ConnectAsync();

        var response = await transport.TransceiveAsync(new byte[] { 0x00, 0xA4, 0x04, 0x00 });

        response.Should().Equal(new byte[] { 0x90, 0x00 });
        handler.Paths.Should().Contain("/api/nfc/apdu");
        // The request body is the outbound APDU as an uppercase hex JSON string.
        handler.Bodies.Should().Contain("\"00A40400\"");
    }

    [Fact]
    public async Task TransceiveAsync_NoContent_ReturnsNull()
    {
        using var transport = Build((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/status")) return Ok("{}");
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }, out _);
        await transport.ConnectAsync();

        var response = await transport.TransceiveAsync(new byte[] { 0x00 });

        response.Should().BeNull();
    }

    [Fact]
    public async Task TransceiveAsync_NotConnected_Throws()
    {
        using var transport = Build((req, _) => Ok("{}"), out _);
        var act = async () => await transport.TransceiveAsync(new byte[] { 0x00 });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── ownsClient disposal ──

    [Fact]
    public void Dispose_OwnedClient_IsDisposed()
    {
        // No client injected -> the transport owns (and must dispose) its own HttpClient.
        var transport = new HttpNfcTransport(BaseUrl);
        var clientField = typeof(HttpNfcTransport)
            .GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance);
        clientField.Should().NotBeNull();
        var client = (HttpClient)clientField!.GetValue(transport)!;

        transport.Dispose();

        // A disposed HttpClient throws ObjectDisposedException before touching the network.
        var act = async () => await client.GetAsync("http://localhost/");
        act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Dispose_InjectedClient_IsNotDisposed()
    {
        var handler = new StubHandler((req, _) => Ok("{}"));
        var client = new HttpClient(handler);
        var transport = new HttpNfcTransport(BaseUrl, client);

        transport.Dispose();

        // The injected client is the caller's to own; the transport must leave it usable.
        var act = async () => await client.GetAsync("http://localhost/");
        await act.Should().NotThrowAsync<ObjectDisposedException>();
        client.Dispose();
    }

    // ── CR-M056 regression: polling faults must surface via PollingError ──

    [Fact]
    public async Task Polling_MalformedTagBody_SurfacesViaPollingError()
    {
        // Status succeeds (connects); the tag endpoint returns HTTP 200 with a non-JSON body, so
        // JsonSerializer.Deserialize throws inside the poll loop. Before CR-M056 that fault killed
        // the detached poll task silently. It must now fire PollingError.
        using var transport = Build((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/status")) return Ok("{}");
            return Ok("<<< not json >>>");
        }, out _);

        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.PollingError += (_, ex) => errorTcs.TrySetResult(ex);

        await transport.ConnectAsync();
        await transport.StartPollingAsync(intervalMs: 50);

        // Bounded wait so a regression (silent death) fails via timeout instead of hanging.
        var completed = await Task.WhenAny(errorTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().BeSameAs(errorTcs.Task, "a poll-loop fault must surface via PollingError, not die silently");
        (await errorTcs.Task).Should().BeOfType<JsonException>();

        // And the loop is torn down cleanly.
        var stop = async () => await transport.StopPollingAsync();
        await stop.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Polling_NonHttpRequestException_SurfacesViaPollingError()
    {
        // A non-HttpRequestException fault (here, the handler throws InvalidOperationException on the
        // tag endpoint) must also surface via PollingError rather than being swallowed.
        using var transport = Build((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/status")) return Ok("{}");
            throw new InvalidOperationException("reader firmware panic");
        }, out _);

        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.PollingError += (_, ex) => errorTcs.TrySetResult(ex);

        await transport.ConnectAsync();
        await transport.StartPollingAsync(intervalMs: 50);

        var completed = await Task.WhenAny(errorTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().BeSameAs(errorTcs.Task, "an unexpected poll-loop fault must surface via PollingError");

        await transport.StopPollingAsync();
    }
}
